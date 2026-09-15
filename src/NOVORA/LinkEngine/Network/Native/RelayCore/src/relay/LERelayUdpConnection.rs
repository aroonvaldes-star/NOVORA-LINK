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

use log::*;/*
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

use log::*;
use mio::net::UdpSocket;
use mio::{Event, PollOpt, Ready, Token};
use std::cell::RefCell;
use std::io;
use std::net::{Ipv4Addr, SocketAddr};
use std::rc::{Rc, Weak};
use std::time::Instant;

use super::binary;
use super::client::{Client, ClientChannel};
use super::connection::{Connection, ConnectionId};
use super::datagram_buffer::DatagramBuffer;
use super::ipv4_header::Ipv4Header;
use super::ipv4_packet::{Ipv4Packet, MAX_PACKET_LENGTH};
use super::packet_source::PacketSource;
use super::packetizer::Packetizer;
use super::selector::Selector;
use super::transport_header::TransportHeader;

const TAG: &str = "UdpConnection";

pub const IDLE_TIMEOUT_SECONDS: u64 = 2 * 60;

/*
 * NOVORA LinkEngine - LowLatency v2
 *
 * El primer LowLatency bajó esta cola desde:
 *
 *     4 * MAX_PACKET_LENGTH
 *
 * hasta:
 *
 *     1 * MAX_PACKET_LENGTH
 *
 * Eso favorece latencia, pero una sola ráfaga UDP puede llenar
 * demasiado rápido la cola y provocar drops.
 *
 * LowLatency-v2 usa un punto intermedio:
 *
 *     2 * MAX_PACKET_LENGTH
 *
 * Seguimos al 50% de la capacidad original de Gnirehtet,
 * pero duplicamos el margen respecto a LowLatency-v1.
 */
const CLIENT_TO_NETWORK_BUFFER_PACKETS: usize = 2;

pub struct UdpConnection {
    /*
     * Referencia débil hacia nosotros mismos.
     *
     * Se utiliza para registrar UdpConnection como PacketSource
     * cuando un paquete ya recibido desde Internet no cabe todavía
     * en Client::network_to_client.
     */
    self_weak: Weak<RefCell<UdpConnection>>,

    id: ConnectionId,

    client: Weak<RefCell<Client>>,

    socket: UdpSocket,

    interests: Ready,

    token: Token,

    /*
     * Android -> Internet.
     */
    client_to_network: DatagramBuffer,

    /*
     * Internet -> Android.
     *
     * Packetizer conserva los bytes del último paquete construido.
     * Esto nos permite retener un paquete temporalmente si el
     * buffer general del Client está lleno.
     */
    network_to_client: Packetizer,

    /*
     * Si contiene Some(length), existe un paquete UDP completo
     * esperando espacio en Client::network_to_client.
     *
     * Mientras haya un paquete pendiente dejamos de leer del
     * socket UDP para evitar sobrescribir el buffer del Packetizer.
     */
    packet_for_client_length: Option<u16>,

    closed: bool,

    idle_since: Instant,
}

impl UdpConnection {
    #[allow(clippy::needless_pass_by_value)]
    pub fn create(
        selector: &mut Selector,
        id: ConnectionId,
        client: Weak<RefCell<Client>>,
        ipv4_header: Ipv4Header,
        transport_header: TransportHeader,
    ) -> io::Result<Rc<RefCell<Self>>> {
        cx_info!(
            target: TAG,
            id,
            "Open"
        );

        let socket =
            Self::create_socket(&id)?;

        let packetizer =
            Packetizer::new(
                &ipv4_header,
                &transport_header,
            );

        /*
         * Inicialmente sólo necesitamos leer.
         */
        let interests =
            Ready::readable();

        let rc =
            Rc::new(
                RefCell::new(
                    Self {
                        self_weak:
                            Weak::new(),

                        id,

                        client,

                        socket,

                        interests,

                        token:
                            Token(0),

                        /*
                         * LowLatency-v2:
                         *
                         * 2 paquetes máximos en lugar del 1x
                         * utilizado durante LowLatency-v1.
                         */
                        client_to_network:
                            DatagramBuffer::new(
                                CLIENT_TO_NETWORK_BUFFER_PACKETS
                                    * MAX_PACKET_LENGTH,
                            ),

                        network_to_client:
                            packetizer,

                        packet_for_client_length:
                            None,

                        closed:
                            false,

                        idle_since:
                            Instant::now(),
                    },
                ),
            );

        {
            let mut self_ref =
                rc.borrow_mut();

            /*
             * Guardamos Weak<Self> para poder introducir este
             * UdpConnection dentro de Client::pending_packet_sources.
             */
            self_ref.self_weak =
                Rc::downgrade(&rc);

            let rc2 =
                rc.clone();

            let handler =
                move |
                    selector: &mut Selector,
                    event
                | {
                    rc2
                        .borrow_mut()
                        .on_ready(
                            selector,
                            event,
                        )
                };

            let token =
                selector.register(
                    &self_ref.socket,
                    handler,
                    interests,
                    PollOpt::level(),
                )?;

            self_ref.token =
                token;
        }

        Ok(rc)
    }

    fn create_socket(
        id: &ConnectionId,
    ) -> io::Result<UdpSocket> {
        let autobind_addr =
            SocketAddr::new(
                Ipv4Addr::new(
                    0,
                    0,
                    0,
                    0,
                )
                .into(),
                0,
            );

        let udp_socket =
            UdpSocket::bind(
                &autobind_addr,
            )?;

        udp_socket.connect(
            id.rewritten_destination()
                .into(),
        )?;

        Ok(udp_socket)
    }

    fn remove_from_router(
        &self,
    ) {
        let client_rc =
            self
                .client
                .upgrade()
                .expect(
                    "Expected client not found",
                );

        let mut client =
            client_rc.borrow_mut();

        client
            .router()
            .remove(self);
    }

    fn on_ready(
        &mut self,
        selector: &mut Selector,
        event: Event,
    ) {
        #[allow(clippy::match_wild_err_arm)]
        match self.process(
            selector,
            event,
        ) {
            Ok(_) => (),

            Err(ref err)
                if err.kind()
                    == io::ErrorKind::WouldBlock =>
            {
                cx_debug!(
                    target: TAG,
                    self.id,
                    "Spurious event, ignoring"
                )
            }

            Err(_) => {
                panic!(
                    "Unexpected unhandled error"
                )
            }
        }
    }

    fn process(
        &mut self,
        selector: &mut Selector,
        event: Event,
    ) -> io::Result<()> {
        if !self.closed {
            self.touch();

            let ready =
                event.readiness();

            if ready.is_readable()
                || ready.is_writable()
            {
                /*
                 * Primero intentamos vaciar tráfico que viene
                 * Android -> Internet.
                 */
                if ready.is_writable() {
                    self.process_send(
                        selector,
                    )?;
                }

                /*
                 * Sólo recibimos otro datagrama desde Internet
                 * si NO tenemos uno pendiente para Android.
                 *
                 * De este modo no sobrescribimos el Packetizer.
                 */
                if !self.closed
                    && ready.is_readable()
                    && self
                        .packet_for_client_length
                        .is_none()
                {
                    self.process_receive(
                        selector,
                    )?;
                }

                if !self.closed {
                    self.update_interests(
                        selector,
                    );
                }
            }
            else {
                /*
                 * error / hup
                 */
                self.close(
                    selector,
                );
            }

            if self.closed {
                self.remove_from_router();
            }
        }

        Ok(())
    }

    fn process_send(
        &mut self,
        selector: &mut Selector,
    ) -> io::Result<()> {
        match self.write() {
            Ok(_) => (),

            Err(ref err)
                if err.kind()
                    == io::ErrorKind::WouldBlock =>
            {
                cx_debug!(
                    target: TAG,
                    self.id,
                    "UDP socket temporarily blocked"
                );
            }

            Err(err) => {
                cx_error!(
                    target: TAG,
                    self.id,
                    "Cannot write: [{:?}] {}",
                    err.kind(),
                    err
                );

                self.close(
                    selector,
                );
            }
        }

        Ok(())
    }

    fn process_receive(
        &mut self,
        selector: &mut Selector,
    ) -> io::Result<()> {
        /*
         * Seguridad adicional:
         *
         * nunca leer otro datagrama mientras existe uno pendiente.
         */
        if self
            .packet_for_client_length
            .is_some()
        {
            return Ok(());
        }

        match self.read(
            selector,
        ) {
            Ok(_) => (),

            Err(err) => {
                if err.kind()
                    == io::ErrorKind::WouldBlock
                {
                    return Err(err);
                }

                cx_error!(
                    target: TAG,
                    self.id,
                    "Cannot read: [{:?}] {}",
                    err.kind(),
                    err
                );

                self.close(
                    selector,
                );
            }
        }

        Ok(())
    }

    /*
     * Internet -> Android
     */
    fn read(
        &mut self,
        selector: &mut Selector,
    ) -> io::Result<()> {
        let ipv4_packet =
            self
                .network_to_client
                .packetize(
                    &mut self.socket,
                )?;

        let packet_length =
            ipv4_packet.length();

        let client_rc =
            self
                .client
                .upgrade()
                .expect(
                    "Expected client not found",
                );

        /*
         * Intentamos entregar directamente al canal Android.
         */
        let send_result =
            client_rc
                .borrow_mut()
                .send_to_client(
                    selector,
                    &ipv4_packet,
                );

        match send_result {
            Ok(_) => {
                cx_debug!(
                    target: TAG,
                    self.id,
                    "Packet ({} bytes) sent to client",
                    packet_length
                );

                if log_enabled!(
                    target: TAG,
                    Level::Trace
                ) {
                    cx_trace!(
                        target: TAG,
                        self.id,
                        "{}",
                        binary::build_packet_string(
                            ipv4_packet.raw(),
                        )
                    );
                }
            }

            /*
             * CAMBIO PRINCIPAL DE LOWLATENCY-v2
             *
             * Antes:
             *
             * Client lleno
             *      ↓
             * warn
             *      ↓
             * DROP UDP
             *
             * Ahora:
             *
             * Client lleno
             *      ↓
             * conservar paquete
             *      ↓
             * registrar PacketSource
             *      ↓
             * Client lo recupera cuando tenga espacio
             */
            Err(ref err)
                if err.kind()
                    == io::ErrorKind::WouldBlock =>
            {
                self.packet_for_client_length =
                    Some(
                        packet_length,
                    );

                let self_rc =
                    self
                        .self_weak
                        .upgrade()
                        .expect(
                            "Expected UDP self reference not found",
                        );

                client_rc
                    .borrow_mut()
                    .register_pending_packet_source(
                        self_rc,
                    );

                cx_debug!(
                    target: TAG,
                    self.id,
                    "Client buffer full; UDP packet deferred instead of dropped"
                );
            }

            Err(err) => {
                /*
                 * Para cualquier fallo diferente de WouldBlock
                 * conservamos el comportamiento defensivo.
                 */
                cx_warn!(
                    target: TAG,
                    self.id,
                    "Cannot send UDP packet to client: {}",
                    err
                );
            }
        }

        Ok(())
    }

    /*
     * Android -> Internet
     */
    fn write(
        &mut self,
    ) -> io::Result<()> {
        if self
            .client_to_network
            .is_empty()
        {
            return Ok(());
        }

        self
            .client_to_network
            .write_to(
                &mut self.socket,
            )?;

        Ok(())
    }

    fn update_interests(
        &mut self,
        selector: &mut Selector,
    ) {
        if self.closed {
            return;
        }

        let mut ready =
            Ready::empty();

        /*
         * Internet -> Android
         *
         * Dejamos de leer mientras un paquete esté esperando
         * espacio en Client.
         */
        if self
            .packet_for_client_length
            .is_none()
        {
            ready |=
                Ready::readable();
        }

        /*
         * Android -> Internet
         */
        if !self
            .client_to_network
            .is_empty()
        {
            ready |=
                Ready::writable();
        }

        cx_debug!(
            target: TAG,
            self.id,
            "interests: {:?}",
            ready
        );

        if self.interests != ready {
            self.interests =
                ready;

            selector
                .reregister(
                    &self.socket,
                    self.token,
                    ready,
                    PollOpt::level(),
                )
                .expect(
                    "Cannot register on poll",
                );
        }
    }

    fn touch(
        &mut self,
    ) {
        self.idle_since =
            Instant::now();
    }
}

impl Connection for UdpConnection {
    fn id(
        &self,
    ) -> &ConnectionId {
        &self.id
    }

    fn send_to_network(
        &mut self,
        selector: &mut Selector,
        _: &mut ClientChannel,
        ipv4_packet: &Ipv4Packet,
    ) {
        match self
            .client_to_network
            .read_from(
                ipv4_packet
                    .payload()
                    .expect(
                        "No payload",
                    ),
            )
        {
            Ok(_) => {
                self.update_interests(
                    selector,
                );
            }

            Err(err) => {
                /*
                 * UDP no tiene retransmisión propia.
                 *
                 * Con 2x tenemos mayor margen de ráfaga que
                 * LowLatency-v1 sin regresar al 4x original.
                 */
                cx_warn!(
                    target: TAG,
                    self.id,
                    "Cannot send to network, drop packet: {}",
                    err
                );
            }
        }
    }

    fn close(
        &mut self,
        selector: &mut Selector,
    ) {
        cx_info!(
            target: TAG,
            self.id,
            "Close"
        );

        self.closed =
            true;

        self.packet_for_client_length =
            None;

        if let Err(err) =
            selector.deregister(
                &self.socket,
                self.token,
            )
        {
            cx_warn!(
                target: TAG,
                self.id,
                "Fail to deregister UDP stream: {:?}",
                err
            );
        }
    }

    fn is_expired(
        &self,
    ) -> bool {
        self
            .idle_since
            .elapsed()
            .as_secs()
            > IDLE_TIMEOUT_SECONDS
    }

    fn is_closed(
        &self,
    ) -> bool {
        self.closed
    }
}

/*
 * NOVORA LinkEngine
 *
 * TCP ya utiliza PacketSource para conservar datos que han sido
 * leídos desde Internet pero todavía no caben en el canal Android.
 *
 * LowLatency-v2 extiende el mismo mecanismo a UDP.
 */
impl PacketSource for UdpConnection {
    fn get(
        &mut self,
    ) -> Option<Ipv4Packet> {
        match self
            .packet_for_client_length
        {
            Some(length) => {
                Some(
                    self
                        .network_to_client
                        .inflate(
                            length,
                        ),
                )
            }

            None => None,
        }
    }

    /*
     * Client llama next() solamente después de haber conseguido
     * introducir correctamente el datagrama pendiente en
     * network_to_client.
     */
    fn next(
        &mut self,
        selector: &mut Selector,
    ) {
        let length =
            self
                .packet_for_client_length
                .expect(
                    "next() called on empty UDP packet source",
                );

        cx_debug!(
            target: TAG,
            self.id,
            "Deferred UDP packet ({} bytes) sent to client",
            length
        );

        /*
         * Ya podemos recibir el siguiente datagrama.
         */
        self.packet_for_client_length =
            None;

        self.touch();

        self.update_interests(
            selector,
        );
    }
}
