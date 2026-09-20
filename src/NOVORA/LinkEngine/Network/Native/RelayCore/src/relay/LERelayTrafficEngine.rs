/*
 * NOVORA-LINK
 * TrafficEngine V1.3
 *
 * Data Plane policy.
 *
 * Congestion, queue pressure and WouldBlock are normal
 * backpressure conditions. They are NOT Recovery conditions.
 */

pub(crate) const MAX_ACTIVE_CLIENTS_LE: usize = 1;

pub(crate) const CLIENT_WRITE_QUANTUM_BYTES_LE: usize =
    16 * 1024;

pub(crate) const CLIENT_QUEUE_HIGH_WATERMARK_PERCENT_LE: usize =
    50;

pub(crate) const MAX_PENDING_SOURCES_PER_PASS_LE: usize =
    32;

pub(crate) fn can_enqueue_to_client_le(
    current_size: usize,
    capacity: usize,
    packet_length: usize,
) -> bool {
    if capacity == 0 {
        return false;
    }

    if packet_length
        > capacity.saturating_sub(current_size)
    {
        return false;
    }

    let high_watermark =
        capacity
            .saturating_mul(
                CLIENT_QUEUE_HIGH_WATERMARK_PERCENT_LE,
            )
            / 100;

    current_size
        .saturating_add(packet_length)
        <= high_watermark
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_packet_below_high_watermark() {
        assert!(
            can_enqueue_to_client_le(
                450,
                1000,
                50,
            )
        );
    }

    #[test]
    fn applies_backpressure_before_full_capacity() {
        assert!(
            !can_enqueue_to_client_le(
                450,
                1000,
                60,
            )
        );
    }

    #[test]
    fn rejects_packet_beyond_physical_capacity() {
        assert!(
            !can_enqueue_to_client_le(
                990,
                1000,
                20,
            )
        );
    }

    #[test]
    fn v21_download_quantum_is_16_kib() {
        assert_eq!(
            16 * 1024,
            CLIENT_WRITE_QUANTUM_BYTES_LE,
        );
    }
}
