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

use log::*;
use mio::net::TcpStream;
use mio::{Event, PollOpt, Ready, Token};
use std::cell::RefCell;
use std::io::{self, Write};
use std::mem;
use std::net::Shutdown;
use std::rc::Rc;

use super::binary;
use super::close_listener::CloseListener;
use super::ipv4_packet::{Ipv4Packet, MAX_PACKET_LENGTH};
use super::ipv4_packet_buffer::Ipv4PacketBuffer;
use super::packet_source::PacketSource;
use super::router::Router;
use super::selector::Selector;
use super::stream_buffer::StreamBuffer;

const TAG: &str = "Client";

/*
 * NOVORA LinkEngine
 *
 * Buffer de salida Relay -> Android.
 *
 * El valor original heredado era:
 *
 *     16 * MAX_PACKET_LENGTH
 *
 * Con MAX_PACKET_LENGTH = 65,536 bytes eso permitÃ­a aproximadamente
 * 1 MiB de trÃ¡fico pendiente por cliente.
 *
 * Para NOVORA-LINK priorizamos latencia y estabilidad bajo carga,
 * por lo que comenzamos con 4 paquetes mÃ¡ximos:
 *
 *     4 * 65,536 = 262,144 bytes
 *
 * Aproximadamente 256 KiB.
 *
 * No reducir mÃ¡s todavÃ­a.
 * Primero debemos comparar:
 *
 * - throughput
 * - loaded latency
 * - jitter
 * - Client buffer full
 * - packet loss
 *
 * contra la lÃ­nea base anterior.
 */
const NETWORK_TO_CLIENT_BUFFER_PACKETS: usize = 4;

pub struct Client {
    id: u32,
    stream: TcpStream,
    interests: Ready,
    token: Token,

    /*
     * Android -> Internet
     */
    client_to_network: Ipv4PacketBuffer,

    /*
     * Internet -> Android
     *
     * Cola limitada para evitar acumulaciones excesivas
     * de trÃ¡fico antes de entregar los paquetes al cliente.
     */
    network_to_client: StreamBuffer,

    router: Router,
    close_listener: Box<dyn CloseListener<Client>>,
    closed: bool,

    /*
     * Fuentes que intentaron enviar un paquete al Android,
     * pero encontraron backpressure.
     */
    pending_packet_sources: std::collections::VecDeque<Rc<RefCell<dyn PacketSource>>>,

    /*
     * Bytes restantes del identificador del cliente.
     * El relay debe enviarlos antes de comenzar a transportar
     * paquetes IPv4.
     */
    pending_id_bytes: usize,
}

/// Canal utilizado por conexiones de red para devolver paquetes
/// inmediatamente al cliente Android.
pub struct ClientChannel<'a> {
    network_to_client: &'a mut StreamBuffer,
    stream: &'a TcpStream,
    token: Token,
    interests: &'a mut Ready,
}

impl<'a> ClientChannel<'a> {
    fn new(
        network_to_client: &'a mut StreamBuffer,
        stream: &'a TcpStream,
        token: Token,
        interests: &'a mut Ready,
    ) -> Self {
        Self {
            network_to_client,
            stream,
            token,
            interests,
        }
    }

    /*
     * Equivalente funcional a Client::send_to_client(),
     * pero sin requerir un prÃ©stamo mutable del Client completo.
     */
    pub fn send_to_client(
        &mut self,
        selector: &mut Selector,
        ipv4_packet: &Ipv4Packet,
    ) -> io::Result<()> {
        let packet_length =
            ipv4_packet.length() as usize;

        if packet_length
            > self.network_to_client.remaining()
            || !super::traffic_engine::can_enqueue_to_client_le(
                self.network_to_client.size(),
                self.network_to_client.capacity(),
                packet_length,
            )
        {
            return Err(
                io::Error::new(
                    io::ErrorKind::WouldBlock,
                    "TrafficEngine backpressure",
                ),
            );
        }

        self.network_to_client
            .read_from(
                ipv4_packet.raw(),
            );

        self.update_interests(
            selector,
        );

        Ok(())
    }
    fn update_interests(&mut self, selector: &mut Selector) {
        let ready = if self.network_to_client.is_empty() {
            Ready::readable()
        } else {
            Ready::readable() | Ready::writable()
        };

        if *self.interests != ready {
            *self.interests = ready;

            selector
                .reregister(
                    self.stream,
                    self.token,
                    ready,
                    PollOpt::level(),
                )
                .expect("Cannot register on poll");
        }
    }
}

impl Client {
    pub fn create(
        id: u32,
        selector: &mut Selector,
        stream: TcpStream,
        close_listener: Box<dyn CloseListener<Client>>,
    ) -> io::Result<Rc<RefCell<Self>>> {
        /*
         * Al comenzar sÃ³lo nos interesa escribir:
         * primero tenemos que enviar el identificador del cliente.
         */
        let interests = Ready::writable();

        let rc = Rc::new(RefCell::new(Self {
            id,
            stream,
            interests,

            /*
             * Se reemplaza posteriormente cuando el Selector
             * registra este Client.
             */
            token: Token(0),

            client_to_network: Ipv4PacketBuffer::new(),

            /*
             * NOVORA LOW-LATENCY CHANGE
             *
             * Antes:
             *
             *     16 * MAX_PACKET_LENGTH
             *
             * Ahora:
             *
             *     4 * MAX_PACKET_LENGTH
             *
             * Esto reduce la cantidad mÃ¡xima de datos que pueden
             * acumularse esperando ser enviados hacia Android.
             */
            network_to_client: StreamBuffer::new(
                NETWORK_TO_CLIENT_BUFFER_PACKETS * MAX_PACKET_LENGTH,
            ),

            router: Router::new(),

            closed: false,

            close_listener,

            pending_packet_sources: std::collections::VecDeque::new(),

            pending_id_bytes: 4,
        }));

        {
            let mut self_ref = rc.borrow_mut();

            /*
             * El Client es propietario del Router.
             */
            self_ref.router.set_client(Rc::downgrade(&rc));

            let rc2 = rc.clone();

            /*
             * Es necesario anotar el tipo del selector.
             */
            let handler =
                move |selector: &mut Selector, event| {
                    rc2.borrow_mut().on_ready(selector, event)
                };

            let token = selector.register(
                &self_ref.stream,
                handler,
                interests,
                PollOpt::level(),
            )?;

            self_ref.token = token;
        }

        Ok(rc)
    }

    pub fn id(&self) -> u32 {
        self.id
    }

    pub fn router(&mut self) -> &mut Router {
        &mut self.router
    }

    pub fn channel(&mut self) -> ClientChannel {
        ClientChannel::new(
            &mut self.network_to_client,
            &self.stream,
            self.token,
            &mut self.interests,
        )
    }

    fn close(&mut self, selector: &mut Selector) {
        self.closed = true;

        selector
            .deregister(&self.stream, self.token)
            .unwrap();

        /*
         * TcpStream no expone close explÃ­cito.
         * shutdown() detiene ambas direcciones;
         * el socket se libera cuando se hace drop.
         */
        if self.stream.shutdown(Shutdown::Both).is_err() {
            warn!(target: TAG, "Cannot shutdown client socket");
        }

        self.router.clear(selector);

        self.close_listener.on_closed(self);
    }

    fn on_ready(&mut self, selector: &mut Selector, event: Event) {
        #[allow(clippy::match_wild_err_arm)]
        match self.process(selector, event) {
            Ok(_) => (),

            Err(ref err)
                if err.kind() == io::ErrorKind::WouldBlock =>
            {
                debug!(
                    target: TAG,
                    "Spurious event, ignoring"
                )
            }

            Err(_) => {
                panic!("Unexpected unhandled error")
            }
        }
    }

    /*
     * Devuelve WouldBlock cuando mio entrega un evento
     * que ya no puede ser procesado inmediatamente.
     */
    fn process(
        &mut self,
        selector: &mut Selector,
        event: Event,
    ) -> io::Result<()> {
        if !self.closed {
            let ready = event.readiness();

            /*
             * ========================================================
             * NOVORA LINKENGINE - FULL DUPLEX FAIRNESS
             * ========================================================
             *
             * El túnel DATA es bidireccional.
             *
             * Primero atendemos Android -> Relay porque el dispositivo
             * ya entregó esos paquetes al túnel y no queremos que una
             * descarga intensa retrase indefinidamente la subida.
             *
             * Después atendemos Relay -> Android.
             *
             * IMPORTANTE:
             *
             * WouldBlock es backpressure normal de sockets
             * non-blocking.
             *
             * Un WouldBlock en una dirección NO evita procesar
             * la otra dirección durante este evento.
             */

            if ready.is_readable() {
                match self.process_receive(selector) {
                    Ok(_) => (),

                    Err(ref err)
                        if err.kind() == io::ErrorKind::WouldBlock =>
                    {
                        debug!(
                            target: TAG,
                            "DATA upstream temporarily blocked for client #{}",
                            self.id
                        );
                    }

                    Err(err) => {
                        return Err(err);
                    }
                }
            }

            if !self.closed && ready.is_writable() {
                match self.process_send(selector) {
                    Ok(_) => (),

                    Err(ref err)
                        if err.kind() == io::ErrorKind::WouldBlock =>
                    {
                        debug!(
                            target: TAG,
                            "DATA downstream temporarily blocked for client #{}",
                            self.id
                        );
                    }

                    Err(err) => {
                        return Err(err);
                    }
                }
            }

            if !self.closed {
                self.update_interests(selector);
            }
        }

        Ok(())
    }

    /*
     * EnvÃ­a datos Relay -> Android.
     */
    fn process_send(
        &mut self,
        selector: &mut Selector,
    ) -> io::Result<()> {
        if self.must_send_id() {
            match self.send_id() {
                Ok(_) => {
                    if self.pending_id_bytes == 0 {
                        debug!(
                            target: TAG,
                            "Client id #{} sent to client",
                            self.id
                        );
                    }
                }

                Err(err) => {
                    if err.kind() == io::ErrorKind::WouldBlock {
                        return Err(err);
                    }

                    error!(
                        target: TAG,
                        "Cannot write client id #{}",
                        self.id
                    );

                    self.close(selector);
                }
            }
        } else {
            match self.write() {
                Ok(_) => {
                    /*
                     * Al liberar espacio intentamos consumir nuevamente
                     * los PacketSource que quedaron bloqueados.
                     */
                    self.process_pending(selector);
                }

                Err(err)
                    if err.kind() == io::ErrorKind::WouldBlock =>
                {
                    /*
                     * Backpressure normal.
                     *
                     * NO cerrar DATA.
                     *
                     * network_to_client conserva la información
                     * pendiente y mio nos volverá a notificar cuando
                     * el socket tenga capacidad para escribir.
                     */
                    return Err(err);
                }

                Err(err) => {
                    /*
                     * NOVORA_TRAFFIC_ENGINE_V1_3
                     *
                     * WouldBlock es backpressure normal.
                     * NO es caída de DATA ni condición de Recovery.
                     *
                     * Retornamos Ok para que Client::process()
                     * pueda continuar con readable en el mismo evento,
                     * protegiendo Android -> Internet.
                     */
                    if err.kind() == io::ErrorKind::WouldBlock {
                        return Ok(());
                    }
                    error!(
                        target: TAG,
                        "Cannot write: [{:?}] {}",
                        err.kind(),
                        err
                    );

                    self.close(selector);
                }
            }
        }

        Ok(())
    }

    /*
     * Recibe paquetes Android -> Relay.
     */
    fn process_receive(
        &mut self,
        selector: &mut Selector,
    ) -> io::Result<()> {
        match self.read() {
            Ok(true) => {
                self.push_to_network(selector);
            }

            Ok(false) => {
                debug!(target: TAG, "EOF reached");

                self.close(selector);
            }

            Err(err) => {
                if err.kind() == io::ErrorKind::WouldBlock {
                    return Err(err);
                }

                error!(
                    target: TAG,
                    "Cannot read: [{:?}] {}",
                    err.kind(),
                    err
                );

                self.close(selector);
            }
        }

        Ok(())
    }

    /*
     * Intenta enviar un paquete IPv4 al cliente Android.
     */
    pub fn send_to_client(
        &mut self,
        selector: &mut Selector,
        ipv4_packet: &Ipv4Packet,
    ) -> io::Result<()> {
        let packet_length =
            ipv4_packet.length() as usize;

        if packet_length
            > self.network_to_client.remaining()
            || !super::traffic_engine::can_enqueue_to_client_le(
                self.network_to_client.size(),
                self.network_to_client.capacity(),
                packet_length,
            )
        {
            return Err(
                io::Error::new(
                    io::ErrorKind::WouldBlock,
                    "TrafficEngine backpressure",
                ),
            );
        }

        self.network_to_client
            .read_from(
                ipv4_packet.raw(),
            );

        self.update_interests(
            selector,
        );

        Ok(())
    }
    pub fn register_pending_packet_source(
        &mut self,
        source: Rc<RefCell<dyn PacketSource>>,
    ) {
        self.pending_packet_sources
            .push_back(source);
    }

    /*
     * EnvÃ­a el ID de 32 bits asignado por el Relay.
     */
    fn send_id(&mut self) -> io::Result<()> {
        assert!(self.must_send_id());

        let raw_id = binary::to_byte_array(self.id);

        let w = self
            .stream
            .write(
                &raw_id[4 - self.pending_id_bytes..],
            )?;

        self.pending_id_bytes -= w;

        Ok(())
    }

    fn update_interests(
        &mut self,
        selector: &mut Selector,
    ) {
        self.channel().update_interests(selector);
    }

    /*
     * Lee bytes desde Android y alimenta el parser IPv4.
     */
    fn read(&mut self) -> io::Result<bool> {
        self.client_to_network
            .read_from(&mut self.stream)
    }

    /*
     * Drena la cola network_to_client hacia el socket Android.
     */
    fn write(
        &mut self,
    ) -> io::Result<usize> {
        let mut written = 0;
        let quantum =
            super::traffic_engine::CLIENT_WRITE_QUANTUM_BYTES_LE;

        while written < quantum && !self.network_to_client.is_empty() {
            match self.network_to_client.write_to_limited(
                &mut self.stream,
                quantum - written,
            ) {
                Ok(0) => break,
                Ok(count) => written += count,
                Err(err) if err.kind() == io::ErrorKind::WouldBlock && written > 0 => break,
                Err(err) => return Err(err),
            }
        }

        Ok(written)
    }
    fn push_to_network(
        &mut self,
        selector: &mut Selector,
    ) {
        while self.push_one_packet_to_network(selector) {
            self.client_to_network.next();
        }
    }

    fn push_one_packet_to_network(
        &mut self,
        selector: &mut Selector,
    ) -> bool {
        match self.client_to_network.as_ipv4_packet() {
            Some(ref packet) => {
                let mut client_channel =
                    ClientChannel::new(
                        &mut self.network_to_client,
                        &self.stream,
                        self.token,
                        &mut self.interests,
                    );

                self.router.send_to_network(
                    selector,
                    &mut client_channel,
                    packet,
                );

                true
            }

            None => false,
        }
    }

    /*
     * Reintenta paquetes que anteriormente encontraron
     * backpressure.
     */
    fn process_pending(
        &mut self,
        selector: &mut Selector,
    ) {
        let attempts =
            self.pending_packet_sources
                .len()
                .min(
                    super::traffic_engine::MAX_PENDING_SOURCES_PER_PASS_LE,
                );

        for _ in 0..attempts {
            let pending =
                match self.pending_packet_sources
                    .pop_front()
                {
                    Some(pending) =>
                        pending,

                    None =>
                        break,
                };

            let consumed = {
                let mut source =
                    pending.borrow_mut();

                let result = {
                    let ipv4_packet =
                        source
                            .get()
                            .expect(
                                "Unexpected pending source with no packet",
                            );

                    self.send_to_client(
                        selector,
                        &ipv4_packet,
                    )
                };

                #[allow(clippy::match_wild_err_arm)]
                match result {
                    Ok(_) => {
                        source.next(
                            selector,
                        );

                        true
                    }

                    Err(ref err)
                        if err.kind()
                            == io::ErrorKind::WouldBlock =>
                    {
                        false
                    }

                    Err(_) => {
                        panic!(
                            "Cannot send packet to client for unknown reason"
                        );
                    }
                }
            };

            if !consumed {
                self.pending_packet_sources
                    .push_back(
                        pending,
                    );
            }
        }
    }
    pub fn clean_expired_connections(
        &mut self,
        selector: &mut Selector,
    ) {
        self.router
            .clean_expired_connections(selector);
    }

    fn must_send_id(&self) -> bool {
        self.pending_id_bytes > 0
    }
}

