// The page's WebRTC, for playing through a room: one RTCPeerConnection per other player, each with one data channel that
// neither orders nor resends (the game's own connection layer does both), handed to the game as numbered peers.
//
// Everything the browser does here is asynchronous and the game asks synchronously, once a frame: so what a connection has
// to tell the other side, and what arrives on its channel, is queued here until the game takes it, and what the other
// side said is applied in the order it came, one step after another.

const CONNECTING = 0;
const OPEN = 1;
const CLOSED = 2;

// Datagrams waiting for the game: a second's worth for a busy host. Past it the oldest go, as a full socket drops them.
const MAX_QUEUED = 512;

const peers = new Map();
let next = 1;

function create(iceJson, offerer, relayOnly) {
  const id = next++;
  const pc = new RTCPeerConnection({ iceServers: JSON.parse(iceJson), iceTransportPolicy: relayOnly ? 'relay' : 'all' });
  const peer = { pc, channel: null, state: CONNECTING, failure: null, relayed: false, signals: [], received: [], chain: Promise.resolve() };
  peers.set(id, peer);

  pc.onicecandidate = e => {
    if (e.candidate && e.candidate.candidate) {
      peer.signals.push({ kind: 'candidate', value: e.candidate.candidate, mid: e.candidate.sdpMid });
    }
  };
  pc.onconnectionstatechange = () => {
    if (pc.connectionState === 'connected') findRelay(peer);
    if (pc.connectionState === 'failed') fail(peer, 'no way through to the other player');
    else if (pc.connectionState === 'closed' || pc.connectionState === 'disconnected') fail(peer, 'the connection dropped');
  };

  if (offerer) {
    attach(peer, pc.createDataChannel('game', { ordered: false, maxRetransmits: 0 }));
    step(peer, async () => {
      await pc.setLocalDescription();
      peer.signals.push({ kind: pc.localDescription.type, value: pc.localDescription.sdp });
    });
  } else {
    pc.ondatachannel = e => attach(peer, e.channel);
  }

  return id;
}

function attach(peer, channel) {
  peer.channel = channel;
  channel.binaryType = 'arraybuffer';
  channel.onopen = () => { if (peer.state === CONNECTING) peer.state = OPEN; };
  channel.onclose = () => fail(peer, 'the other player closed the channel');
  channel.onerror = () => fail(peer, 'channel error');
  channel.onmessage = e => {
    if (typeof e.data === 'string') return;
    peer.received.push(new Uint8Array(e.data));
    if (peer.received.length > MAX_QUEUED) peer.received.shift();
  };
}

// Whether the pair ICE settled on goes through the relay: a relay candidate at either end.
async function findRelay(peer) {
  try {
    const stats = await peer.pc.getStats();
    let pair = null;
    stats.forEach(s => {
      if (s.type === 'transport' && s.selectedCandidatePairId) pair = stats.get(s.selectedCandidatePairId);
    });
    if (!pair) stats.forEach(s => { if (s.type === 'candidate-pair' && s.nominated && s.state === 'succeeded') pair = s; });
    const local = pair && stats.get(pair.localCandidateId);
    const remote = pair && stats.get(pair.remoteCandidateId);
    peer.relayed = local?.candidateType === 'relay' || remote?.candidateType === 'relay';
  } catch {
    peer.relayed = false;
  }
}

function fail(peer, why) {
  peer.failure ??= why;
  peer.state = CLOSED;
}

// The other side's offer, answer and candidates, in the order they came: a candidate can't be added before the
// description it belongs to is in.
function step(peer, work) {
  peer.chain = peer.chain.then(work).catch(e => fail(peer, String(e?.message ?? e)));
}

export const imports = {
  create: (iceJson, offerer, relayOnly) => create(iceJson, offerer, relayOnly),

  setRemote: (id, kind, sdp) => {
    const peer = peers.get(id);
    if (!peer) return;
    step(peer, async () => {
      await peer.pc.setRemoteDescription({ type: kind, sdp });
      if (kind === 'offer') {
        await peer.pc.setLocalDescription();
        peer.signals.push({ kind: peer.pc.localDescription.type, value: peer.pc.localDescription.sdp });
      }
    });
  },

  addCandidate: (id, candidate, mid) => {
    const peer = peers.get(id);
    if (!peer) return;
    step(peer, () => peer.pc.addIceCandidate({ candidate, sdpMid: mid ?? '0' }));
  },

  // The next thing to tell the other side, as JSON, or null.
  takeSignal: id => {
    const signal = peers.get(id)?.signals.shift();
    return signal ? JSON.stringify(signal) : null;
  },

  state: id => peers.get(id)?.state ?? CLOSED,

  failure: id => peers.get(id)?.failure ?? null,
  relayed: id => peers.get(id)?.relayed === true,

  send: (id, view) => {
    const peer = peers.get(id);
    if (peer?.state === OPEN && peer.channel?.readyState === 'open') {
      try {
        peer.channel.send(view.slice());
      } catch {
        // A full send buffer drops the datagram, as a network would.
      }
    }
  },

  // Copies the next datagram into the game's buffer; its length, or -1 when none is waiting.
  receive: (id, view) => {
    const peer = peers.get(id);
    while (peer?.received.length) {
      const datagram = peer.received.shift();
      if (datagram.length <= view.length) {
        view.set(datagram);
        return datagram.length;
      }
    }

    return -1;
  },

  close: id => {
    const peer = peers.get(id);
    peers.delete(id);
    if (peer) {
      peer.state = CLOSED;
      peer.channel?.close();
      peer.pc.close();
    }
  },
};
