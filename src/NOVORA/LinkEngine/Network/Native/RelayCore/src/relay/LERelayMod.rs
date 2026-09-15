/*
 * Copyright (C) 2017 Genymobile
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

pub use self::relay::Relay;
#[path = "LERelayByteBuffer.rs"]
pub mod byte_buffer;

#[path = "LERelayBinary.rs"]
mod binary;
#[path = "LERelayClient.rs"]
mod client;
#[path = "LERelayCloseListener.rs"]
mod close_listener;
#[macro_use]
#[path = "LERelayConnection.rs"]
mod connection;
#[path = "LERelayDatagram.rs"]
mod datagram;
#[path = "LERelayDatagramBuffer.rs"]
mod datagram_buffer;
#[macro_use]
#[path = "LERelayInterrupt.rs"]
mod interrupt;
#[path = "LERelayIpv4Header.rs"]
mod ipv4_header;
#[path = "LERelayIpv4Packet.rs"]
mod ipv4_packet;
#[path = "LERelayIpv4PacketBuffer.rs"]
mod ipv4_packet_buffer;
#[path = "LERelayNet.rs"]
mod net;
#[path = "LERelayPacketSource.rs"]
mod packet_source;
#[path = "LERelayPacketizer.rs"]
mod packetizer;
#[allow(clippy::module_inception)] // relay.rs is in relay/
#[path = "LERelayRelay.rs"]
mod relay;
#[path = "LERelayRouter.rs"]
mod router;
#[path = "LERelaySelector.rs"]
mod selector;
#[path = "LERelayStreamBuffer.rs"]
mod stream_buffer;
#[path = "LERelayTrafficEngine.rs"]
mod traffic_engine;
#[path = "LERelayTcpConnection.rs"]
mod tcp_connection;
#[path = "LERelayTcpHeader.rs"]
mod tcp_header;
#[path = "LERelayTransportHeader.rs"]
mod transport_header;
#[path = "LERelayTunnelServer.rs"]
mod tunnel_server;
#[path = "LERelayUdpConnection.rs"]
mod udp_connection;
#[path = "LERelayUdpHeader.rs"]
mod udp_header;
