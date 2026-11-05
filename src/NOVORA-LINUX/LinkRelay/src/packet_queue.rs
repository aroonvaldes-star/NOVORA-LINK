use crate::flow_scheduler::{FlowKey, PacketQueue, QueueError, QueuedPacket};
use crate::policy::{invalid, Direction, Domain, PolicyState};
use crate::telemetry::Telemetry;
use std::io;
use std::sync::{Arc, Mutex};
use tokio::sync::{Notify, Semaphore};

/// The shared byte semaphore covers both packet directions and UDP proxy queues.
/// tcp_ip's own reservoirs remain separate and are intentionally not retuned here.
pub struct PacketPipe {
    queue: Mutex<PacketQueue>,
    ready: Notify,
    space: Notify,
    pub memory: Arc<Semaphore>,
    direction: Direction,
}

impl PacketPipe {
    pub fn new(memory: Arc<Semaphore>, direction: Direction) -> Self {
        Self {
            queue: Mutex::new(PacketQueue::new()),
            ready: Notify::new(),
            space: Notify::new(),
            memory,
            direction,
        }
    }
    pub async fn enqueue(
        &self,
        bytes: Vec<u8>,
        policy: &PolicyState,
        telemetry: &Telemetry,
    ) -> io::Result<()> {
        let key = FlowKey::parse(&bytes)?;
        let udp = key.is_udp();
        loop {
            let notified = self.space.notified();
            tokio::pin!(notified);
            notified.as_mut().enable();
            let error = self
                .queue
                .lock()
                .unwrap()
                .can_enqueue(&key, bytes.len())
                .err();
            if let Some(error) = error {
                if udp && policy.shaping() {
                    telemetry.record_drop_at(
                        Domain::DeviceLink,
                        self.direction,
                        drop_reason(error),
                    );
                    return Ok(());
                }
                notified.await;
                continue;
            }
            let permit = if udp && policy.shaping() {
                match self
                    .memory
                    .clone()
                    .try_acquire_many_owned(bytes.len() as u32)
                {
                    Ok(permit) => permit,
                    Err(_) => {
                        telemetry.record_drop_at(
                            Domain::DeviceLink,
                            self.direction,
                            "udp_queue_full",
                        );
                        return Ok(());
                    }
                }
            } else {
                self.memory
                    .clone()
                    .acquire_many_owned(bytes.len() as u32)
                    .await
                    .map_err(|_| invalid("packet queue closed"))?
            };
            {
                let mut queue = self.queue.lock().unwrap();
                queue
                    .enqueue(bytes, policy.elapsed())
                    .map_err(|_| invalid("packet queue admission changed"))?;
                queue.attach_last_permit(&key, permit);
            }
            self.ready.notify_one();
            return Ok(());
        }
    }
    pub async fn dequeue(&self, policy: &PolicyState, telemetry: &Telemetry) -> QueuedPacket {
        loop {
            let notified = self.ready.notified();
            tokio::pin!(notified);
            notified.as_mut().enable();
            let packet = {
                let mut queue = self.queue.lock().unwrap();
                let packet = if policy.shaping() {
                    queue.dequeue(policy.elapsed())
                } else {
                    queue.dequeue_fifo()
                };
                if let Some(ref packet) = packet {
                    telemetry.record_queue(
                        Domain::DeviceLink,
                        self.direction,
                        queue.queued_bytes(),
                        policy.elapsed().saturating_sub(packet.enqueued_at),
                    );
                }
                packet
            };
            if let Some(packet) = packet {
                self.space.notify_waiters();
                return packet;
            }
            notified.await;
        }
    }
    pub fn sample(&self, policy: &PolicyState, telemetry: &Telemetry) {
        let queue = self.queue.lock().unwrap();
        telemetry.set_queue(
            Domain::DeviceLink,
            self.direction,
            queue.queued_bytes(),
            queue.age_p95(policy.elapsed()),
        );
    }
}

fn drop_reason(error: QueueError) -> &'static str {
    match error {
        QueueError::FlowLimit => "flow_limit",
        QueueError::FlowBytes => "flow_bytes",
        QueueError::TotalBytes => "queue_bytes",
        QueueError::InvalidPacket => "invalid_packet",
    }
}
