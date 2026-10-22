use crate::policy::{Direction, Domain, PolicyState, SessionGeneration, TrafficClass};
use crate::telemetry::Telemetry;
use std::io;
use std::sync::Arc;
use std::time::Instant;
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};

pub async fn copy_proxy<C, U>(
    client: &mut C,
    upstream: &mut U,
    policy: Arc<PolicyState>,
    telemetry: Arc<Telemetry>,
    generation: Arc<SessionGeneration>,
    internet: bool,
) -> io::Result<()>
where
    C: AsyncRead + AsyncWrite + Unpin,
    U: AsyncRead + AsyncWrite + Unpin,
{
    let (client_read, client_write) = tokio::io::split(client);
    let (upstream_read, upstream_write) = tokio::io::split(upstream);
    tokio::try_join!(
        copy_one(
            client_read,
            upstream_write,
            policy.clone(),
            telemetry.clone(),
            generation.clone(),
            internet,
            Direction::FromAndroid
        ),
        copy_one(
            upstream_read,
            client_write,
            policy,
            telemetry,
            generation,
            internet,
            Direction::ToAndroid
        ),
    )?;
    Ok(())
}

async fn copy_one<R, W>(
    mut reader: R,
    mut writer: W,
    policy: Arc<PolicyState>,
    telemetry: Arc<Telemetry>,
    generation: Arc<SessionGeneration>,
    internet: bool,
    direction: Direction,
) -> io::Result<()>
where
    R: AsyncRead + Unpin,
    W: AsyncWrite + Unpin,
{
    let mut buffer = [0u8; 1500];
    let mut changed = policy.subscribe();
    loop {
        let chunk = policy.chunk_size(Domain::Internet, direction, TrafficClass::BestEffort);
        if internet && direction == Direction::ToAndroid {
            // Availability gates the next upstream read without a debit. A debit before
            // a slow read accumulates permissions and bursts when all upstreams resume.
            policy
                .wait_available(
                    Domain::Internet,
                    direction,
                    TrafficClass::BestEffort,
                    chunk,
                    &generation,
                    &mut changed,
                )
                .await?;
        }
        let count = reader.read(&mut buffer[..chunk]).await?;
        if count == 0 {
            return writer.shutdown().await;
        }
        let pending = internet.then(|| telemetry.buffer(Domain::Internet, direction, count));
        if internet {
            // One actual-byte debit, shared with TCP/UDP, immediately before sending.
            policy
                .wait_session(
                    Domain::Internet,
                    direction,
                    TrafficClass::BestEffort,
                    count,
                    &generation,
                    &mut changed,
                )
                .await?;
        }
        if let Some(buffer) = &pending {
            buffer.writing();
        }
        let start = Instant::now();
        writer.write_all(&buffer[..count]).await?;
        if internet {
            telemetry.record_wait(Domain::Internet, direction, start.elapsed());
            telemetry.record_bytes(Domain::Internet, direction, count as u64, 0);
        }
    }
}
