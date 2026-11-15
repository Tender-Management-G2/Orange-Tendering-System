import * as THREE from 'three';

// The eleven supplier categories as modular miniature assets on one 9 × 8 module pad.
// One kit (ceramic, kraft board, graphite, champagne metal, glass, orange accents), one scale
// (a desk is ~1.6 units long, a storey ~1.5), so every category belongs to the same enterprise.
// Each asset is a list of `parts`: groups standing on the pad (y = 0 is their footing), raised in
// that order by the campus; `tick(t)` adds a little restrained operational motion where the
// business has some (a crane slewing, a forklift shuttling, server lights, a beacon).
//
// Names must match Models/Entities/SupplierCategories.cs.

const PAD_Y = 0.14;

function kit(m) {
  const mesh = (geo, mat) => {
    const o = new THREE.Mesh(geo, mat);
    o.castShadow = true;
    o.receiveShadow = true;
    return o;
  };
  const box = (w, h, d, mat, x = 0, y = 0, z = 0) => {
    const o = mesh(new THREE.BoxGeometry(w, h, d), mat);
    o.position.set(x, y + h / 2, z);
    return o;
  };
  const cyl = (r, h, mat, x = 0, y = 0, z = 0, seg = 28, r2 = r) => {
    const o = mesh(new THREE.CylinderGeometry(r, r2, h, seg), mat);
    o.position.set(x, y + h / 2, z);
    return o;
  };
  const part = (x, z, rotY = 0, ...children) => {
    const g = new THREE.Group();
    g.position.set(x, PAD_Y, z);
    g.rotation.y = rotY;
    g.add(...children);
    return g;
  };
  const crate = (w, h, d, x, y, z) => {
    const g = new THREE.Group();
    g.add(box(w, h, d, m.kraft, 0, 0, 0), box(w + 0.02, 0.05, d * 0.22, m.orange, 0, h * 0.62, 0));
    g.position.set(x, y, z);
    return g;
  };
  const shelf = (w, h, d, levels, mat = m.ceramic) => {
    const g = new THREE.Group();
    g.add(box(0.08, h, d, mat, -w / 2, 0, 0), box(0.08, h, d, mat, w / 2, 0, 0));
    for (let i = 0; i < levels; i++) g.add(box(w, 0.06, d, mat, 0, (i * (h - 0.06)) / (levels - 1), 0));
    return g;
  };
  const desk = (w = 1.6, d = 0.8) => {
    const g = new THREE.Group();
    g.add(box(w, 0.06, d, m.ceramic, 0, 0.74, 0));
    for (const sx of [-1, 1]) g.add(box(0.06, 0.74, d - 0.1, m.graphite, sx * (w / 2 - 0.08), 0, 0));
    return g;
  };
  const chair = () => {
    const g = new THREE.Group();
    g.add(cyl(0.05, 0.42, m.graphite), box(0.5, 0.07, 0.5, m.graphite, 0, 0.42, 0), box(0.5, 0.55, 0.07, m.graphite, 0, 0.49, -0.22));
    return g;
  };
  const monitor = () => {
    const g = new THREE.Group();
    g.add(box(0.12, 0.25, 0.12, m.graphite), box(0.72, 0.44, 0.04, m.glass, 0, 0.25, 0));
    return g;
  };
  const paperStack = (n, x, z) => {
    const g = new THREE.Group();
    for (let i = 0; i < n; i++) {
      const s = box(0.6, 0.05, 0.82, m.paper, 0, i * 0.05, 0);
      s.rotation.y = (i % 3) * 0.06 - 0.06;
      g.add(s);
    }
    g.position.set(x, 0, z);
    return g;
  };
  return { box, cyl, part, crate, shelf, desk, chair, monitor, paperStack };
}

const BUILDERS = {
  'IT & Electronics'(m, k) {
    const leds = [];
    const rack = x => {
      const g = new THREE.Group();
      g.add(k.box(1.0, 2.5, 1.3, m.graphite));
      for (let i = 0; i < 7; i++) {
        g.add(k.box(0.86, 0.22, 0.02, m.glass, 0, 0.18 + i * 0.32, 0.66));
        const led = k.box(0.08, 0.05, 0.03, new THREE.MeshBasicMaterial({ color: i % 3 ? '#7CC59A' : '#FF9A3D', toneMapped: false }), -0.32, 0.27 + i * 0.32, 0.68);
        led.castShadow = false;
        g.add(led);
        leds.push(led);
      }
      g.add(k.box(1.04, 0.06, 1.34, m.champagne, 0, 2.5, 0));
      return k.part(x, -2.1, 0, g);
    };
    const parts = [rack(-3.0), rack(-1.8), rack(-0.6)];
    const ws = k.desk(1.8);
    ws.add(k.monitor().translateY(0.8).translateX(-0.35), k.monitor().translateY(0.8).translateX(0.45));
    parts.push(k.part(2.0, -2.0, 0, ws), k.part(2.0, -1.1, Math.PI, k.chair()));
    // Network patch panel with orange fibres running to the racks.
    const cab = new THREE.Group();
    cab.add(k.box(1.4, 1.1, 0.7, m.ceramic), k.box(1.2, 0.08, 0.02, m.orange, 0, 0.9, 0.36), k.box(1.2, 0.08, 0.02, m.orange, 0, 0.7, 0.36));
    parts.push(k.part(3.2, 1.3, -0.3, cab));
    const crates = new THREE.Group();
    crates.add(k.crate(1.2, 0.8, 1.0, 0, 0, 0), k.crate(1.0, 0.7, 0.9, 1.25, 0, 0.1), k.crate(0.9, 0.6, 0.8, 0.5, 0.8, 0));
    parts.push(k.part(-2.4, 1.6, 0.15, crates));
    return {
      parts,
      tick(t) {
        leds.forEach((l, i) => (l.visible = Math.sin(t * (1.3 + (i % 5) * 0.37) + i * 1.7) > -0.35));
        return true;
      }
    };
  },

  'Office Supplies'(m, k) {
    const parts = [];
    for (const x of [-3.0, -1.4]) {
      const s = k.shelf(1.4, 2.3, 0.7, 4);
      for (let lv = 0; lv < 3; lv++) for (let i = 0; i < 3; i++) s.add(k.box(0.34, 0.42, 0.5, i % 2 ? m.kraft : m.paper, -0.45 + i * 0.45, 0.06 + lv * 0.75, 0));
      parts.push(k.part(x, -2.4, 0, s));
    }
    const printer = new THREE.Group();
    printer.add(k.box(1.2, 0.7, 0.9, m.ceramic), k.box(1.0, 0.05, 0.5, m.paper, 0, 0.72, 0.1), k.box(0.9, 0.06, 0.08, m.orange, 0, 0.5, 0.46));
    parts.push(k.part(1.3, -2.3, 0, printer));
    parts.push(k.part(0.5, 0.8, 0, k.paperStack(9, 0, 0), k.paperStack(6, 0.75, 0.1), k.paperStack(11, -0.75, -0.05)));
    const pack = k.desk(2.2, 1.0);
    pack.add(k.crate(0.8, 0.5, 0.6, -0.5, 0.8, 0), k.crate(0.6, 0.4, 0.5, 0.45, 0.8, 0.1));
    parts.push(k.part(2.8, 1.4, -0.2, pack));
    return { parts };
  },

  'Furniture & Fixtures'(m, k) {
    const parts = [];
    const table = new THREE.Group();
    table.add(k.box(2.6, 0.08, 1.2, m.kraftIn, 0, 0.72, 0));
    for (const [x, z] of [[-1.15, -0.45], [1.15, -0.45], [-1.15, 0.45], [1.15, 0.45]]) table.add(k.box(0.08, 0.72, 0.08, m.graphite, x, 0, z));
    parts.push(k.part(-1.4, -1.4, 0, table));
    for (const [x, z, r] of [[-2.2, -0.5, Math.PI], [-0.6, -0.5, Math.PI], [-2.2, -2.3, 0], [-0.6, -2.3, 0]]) parts.push(k.part(x, z, r, k.chair()));
    const desk = k.desk(1.8, 0.9);
    desk.add(k.box(0.5, 0.6, 0.8, m.kraft, 0.6, 0.0, 0));
    parts.push(k.part(2.4, -2.2, 0, desk));
    const shelf = k.shelf(1.6, 1.8, 0.5, 3, m.kraftIn);
    parts.push(k.part(2.5, 0.9, -Math.PI / 2, shelf));
    // Material samples: wood, fabric and metal swatches on a low plinth.
    const swatch = new THREE.Group();
    swatch.add(k.box(1.6, 0.3, 0.8, m.ceramic));
    [m.kraft, m.graphite, m.champagne, m.orange].forEach((mat, i) => swatch.add(k.box(0.3, 0.04, 0.5, mat, -0.55 + i * 0.37, 0.3, 0)));
    parts.push(k.part(0.0, 2.2, 0, swatch));
    return { parts };
  },

  'Construction & Maintenance'(m, k) {
    const parts = [];
    // Crane: tower, slewing jib with counterweight, a hook carrying a beam.
    const tower = new THREE.Group();
    for (let i = 0; i < 6; i++) {
      tower.add(k.box(0.5, 0.05, 0.5, m.orange, 0, i * 0.7, 0));
      for (const [x, z] of [[-0.22, -0.22], [0.22, -0.22], [-0.22, 0.22], [0.22, 0.22]]) tower.add(k.box(0.05, 0.7, 0.05, m.orange, x, i * 0.7, z));
    }
    const slew = new THREE.Group();
    slew.position.y = 4.2;
    slew.add(k.box(4.6, 0.18, 0.3, m.orange, 1.2, 0, 0), k.box(0.8, 0.5, 0.6, m.graphite, -0.9, -0.1, 0), k.box(0.5, 0.6, 0.5, m.ceramic, 0, 0.18, 0));
    const cable = k.box(0.02, 1.6, 0.02, m.graphite, 3.0, -1.6, 0);
    const beam = k.box(1.4, 0.16, 0.24, m.champagne, 3.0, -1.8, 0);
    slew.add(cable, beam);
    tower.add(slew);
    parts.push(k.part(-2.8, -1.8, 0, tower));
    const frame = new THREE.Group();
    for (const [x, z] of [[-1, -0.8], [1, -0.8], [-1, 0.8], [1, 0.8]]) frame.add(k.box(0.12, 2.0, 0.12, m.ceramic, x, 0, z));
    frame.add(k.box(2.24, 0.12, 1.84, m.ceramic, 0, 2.0, 0), k.box(2.24, 0.1, 1.84, m.kraftIn, 0, 1.0, 0));
    parts.push(k.part(1.6, -1.6, 0, frame));
    const planks = new THREE.Group();
    for (let i = 0; i < 5; i++) planks.add(k.box(2.2, 0.12, 0.3, m.kraft, 0, i * 0.12, -0.4 + (i % 2) * 0.35));
    parts.push(k.part(2.0, 1.8, 0.1, planks));
    const blocks = new THREE.Group();
    for (let i = 0; i < 6; i++) blocks.add(k.box(0.5, 0.35, 0.35, m.ceramicEdge, (i % 3) * 0.55 - 0.55, Math.floor(i / 3) * 0.35, 0));
    parts.push(k.part(-1.0, 2.0, 0, blocks));
    return {
      parts,
      tick(t) {
        slew.rotation.y = Math.sin(t * 0.35) * 0.55;
        const drop = 1.2 + Math.sin(t * 0.7) * 0.35;
        cable.scale.y = drop / 1.6;
        cable.position.y = -drop;
        beam.position.y = -drop - 0.2;
        return true;
      }
    };
  },

  'Transport & Logistics'(m, k) {
    const parts = [];
    const rack = new THREE.Group();
    for (const x of [-1.6, 0, 1.6]) rack.add(k.box(0.1, 2.4, 1.0, m.orange, x, 0, 0));
    for (const y of [0.1, 1.2, 2.3]) rack.add(k.box(3.3, 0.06, 1.0, m.graphite, 0, y, 0));
    for (const [x, y] of [[-0.8, 0.16], [0.8, 0.16], [-0.8, 1.26], [0.8, 1.26]]) rack.add(k.box(1.1, 0.12, 0.9, m.kraftIn, x, y, 0), k.crate(0.9, 0.6, 0.7, x, y + 0.12, 0));
    parts.push(k.part(-1.6, -2.6, 0, rack));
    // Forklift with mast and forks.
    const lift = new THREE.Group();
    lift.add(k.box(0.9, 0.5, 1.2, m.orange, 0, 0.15, 0), k.box(0.8, 0.7, 0.04, m.glass, 0, 0.65, -0.1), k.box(0.9, 0.08, 0.9, m.graphite, 0, 1.3, -0.1));
    for (const x of [-0.35, 0.35]) lift.add(k.cyl(0.18, 0.12, m.graphite, x, 0, 0.4).rotateZ(Math.PI / 2), k.box(0.08, 1.6, 0.08, m.graphite, x, 0.1, 0.65), k.box(0.1, 0.05, 0.8, m.champagne, x * 0.8, 0.2, 1.05));
    lift.add(k.crate(0.8, 0.5, 0.7, 0, 0.25, 1.05));
    const fork = k.part(1.8, -0.4, -Math.PI / 2, lift);
    parts.push(fork);
    // Delivery truck: cab and box body.
    const truck = new THREE.Group();
    truck.add(k.box(2.6, 1.5, 1.3, m.ceramic, -0.4, 0.3, 0), k.box(1.0, 1.1, 1.24, m.orange, 1.45, 0.3, 0), k.box(0.04, 0.5, 1.0, m.glass, 1.96, 0.8, 0), k.box(2.6, 0.06, 1.32, m.orange, -0.4, 1.0, 0));
    for (const x of [-1.2, 0.3, 1.5]) for (const z of [-0.6, 0.6]) truck.add(k.cyl(0.3, 0.22, m.graphite, x, 0.0, z).rotateX(Math.PI / 2).translateY(0.0));
    parts.push(k.part(1.2, 2.3, 0, truck));
    parts.push(k.part(-2.8, 1.6, 0, k.crate(1.0, 0.7, 0.9, 0, 0, 0), k.crate(0.9, 0.6, 0.8, 1.0, 0, 0.2), k.crate(0.8, 0.5, 0.7, 0.4, 0.7, 0.1)));
    return {
      parts,
      tick(t) {
        fork.position.x = 1.8 + Math.sin(t * 0.45) * 1.0;
        return true;
      }
    };
  },

  'Professional Services'(m, k) {
    const parts = [];
    const meet = new THREE.Group();
    meet.add(k.cyl(1.05, 0.06, m.ceramic, 0, 0.72, 0, 48), k.cyl(0.12, 0.72, m.graphite));
    meet.add(k.paperStack(3, -0.3, 0.1).translateY(0.78), k.paperStack(2, 0.4, -0.2).translateY(0.78));
    parts.push(k.part(-1.8, -1.2, 0, meet));
    for (let i = 0; i < 5; i++) {
      const a = (i / 5) * Math.PI * 2 + 0.3;
      parts.push(k.part(-1.8 + Math.cos(a) * 1.45, -1.2 + Math.sin(a) * 1.45, -a - Math.PI / 2, k.chair()));
    }
    for (const x of [1.0, 2.9]) {
      const d = k.desk(1.6, 0.8);
      d.add(k.monitor().translateY(0.8), k.paperStack(4, 0.55, 0.15).translateY(0.8));
      parts.push(k.part(x, -2.4, 0, d), k.part(x, -1.6, Math.PI, k.chair()));
    }
    const board = new THREE.Group();
    board.add(k.box(2.4, 1.4, 0.06, m.paper, 0, 0.6, 0), k.box(0.06, 0.6, 0.06, m.graphite, -1.0, 0, 0), k.box(0.06, 0.6, 0.06, m.graphite, 1.0, 0, 0), k.box(1.4, 0.04, 0.07, m.orange, -0.3, 1.5, 0), k.box(0.9, 0.04, 0.07, m.graphite, -0.55, 1.2, 0));
    parts.push(k.part(2.0, 1.5, 0, board));
    parts.push(k.part(-2.9, 2.2, 0, k.paperStack(10, 0, 0), k.paperStack(7, 0.8, 0)));
    return { parts };
  },

  'Medical & Safety'(m, k) {
    const parts = [];
    const cross = (g, x, y, z) => g.add(k.box(0.36, 0.1, 0.02, m.orange, x, y, z), k.box(0.1, 0.36, 0.02, m.orange, x, y - 0.13, z));
    for (const x of [-3.0, -1.7, -0.4]) {
      const cab = new THREE.Group();
      cab.add(k.box(1.1, 2.2, 0.7, m.ceramic), k.box(0.02, 2.0, 0.02, m.ceramicEdge, 0, 0.1, 0.36));
      cross(cab, 0, 1.75, 0.36);
      parts.push(k.part(x, -2.5, 0, cab));
    }
    const inspect = new THREE.Group();
    inspect.add(k.box(2.0, 0.8, 0.9, m.ceramic), k.box(2.0, 0.05, 0.9, m.champagne, 0, 0.8, 0), k.cyl(0.05, 1.0, m.graphite, 0.8, 0.85, -0.3), k.cyl(0.25, 0.08, m.glass, 0.8, 1.85, -0.3));
    parts.push(k.part(2.2, -2.0, 0, inspect));
    const kits = new THREE.Group();
    for (let i = 0; i < 4; i++) {
      const b = new THREE.Group();
      b.add(k.box(0.8, 0.5, 0.6, m.paper));
      cross(b, 0, 0.42, 0.31);
      b.position.set((i % 2) * 0.9, Math.floor(i / 2) * 0.5, 0);
      kits.add(b);
    }
    parts.push(k.part(-2.2, 1.6, 0, kits));
    const vests = new THREE.Group();
    vests.add(k.box(1.8, 0.05, 0.05, m.graphite, 0, 1.6, 0), k.box(0.05, 1.6, 0.05, m.graphite, -0.85, 0, 0), k.box(0.05, 1.6, 0.05, m.graphite, 0.85, 0, 0));
    for (const x of [-0.5, 0, 0.5]) vests.add(k.box(0.4, 0.6, 0.1, m.orange, x, 0.9, 0), k.box(0.4, 0.05, 0.11, m.champagne, x, 1.15, 0));
    parts.push(k.part(1.6, 1.6, 0, vests));
    return { parts };
  },

  'Food & Hospitality'(m, k) {
    const parts = [];
    const counter = new THREE.Group();
    counter.add(k.box(3.2, 0.85, 0.9, m.ceramicEdge), k.box(3.3, 0.06, 1.0, m.champagne, 0, 0.85, 0));
    for (let i = 0; i < 4; i++) counter.add(k.cyl(0.18, 0.3, m.ceramic, -1.2 + i * 0.8, 0.91, 0, 24));
    parts.push(k.part(-1.6, -2.4, 0, counter));
    for (const x of [1.5, 2.8]) {
      const fridge = new THREE.Group();
      fridge.add(k.box(1.1, 2.3, 0.9, m.ceramic), k.box(0.06, 0.8, 0.06, m.champagne, 0.4, 1.2, 0.47), k.box(1.0, 0.02, 0.02, m.ceramicEdge, 0, 1.0, 0.46));
      parts.push(k.part(x, -2.5, 0, fridge));
    }
    const containers = new THREE.Group();
    for (let i = 0; i < 6; i++) containers.add(k.cyl(0.32, 0.5, i % 2 ? m.paper : m.kraftIn, (i % 3) * 0.75 - 0.75, Math.floor(i / 3) * 0.5, 0, 24), k.cyl(0.33, 0.05, m.orange, (i % 3) * 0.75 - 0.75, Math.floor(i / 3) * 0.5 + 0.48, 0, 24));
    parts.push(k.part(-2.2, 1.4, 0, containers));
    const trolley = new THREE.Group();
    for (const y of [0.25, 0.85]) trolley.add(k.box(1.4, 0.05, 0.8, m.champagne, 0, y, 0));
    trolley.add(k.box(0.05, 1.0, 0.05, m.graphite, -0.68, 0, -0.38), k.box(0.05, 1.0, 0.05, m.graphite, 0.68, 0, 0.38), k.crate(0.6, 0.35, 0.5, -0.3, 0.9, 0), k.crate(0.5, 0.3, 0.45, 0.35, 0.3, 0));
    parts.push(k.part(1.6, 1.4, 0.3, trolley));
    return { parts };
  },

  'Cleaning & Facilities'(m, k) {
    const parts = [];
    const cart = new THREE.Group();
    cart.add(k.box(1.4, 0.06, 0.8, m.graphite, 0, 0.2, 0), k.box(1.4, 0.06, 0.8, m.graphite, 0, 0.9, 0), k.cyl(0.32, 0.6, m.orange, -0.35, 0.26, 0, 24), k.cyl(0.28, 0.5, m.ceramic, 0.4, 0.26, 0, 24), k.box(0.05, 0.6, 0.05, m.champagne, 0.7, 0.9, 0));
    parts.push(k.part(-2.4, -1.6, 0.2, cart));
    const scrub = new THREE.Group();
    scrub.add(k.box(1.0, 0.7, 1.3, m.ceramic, 0, 0.1, 0), k.cyl(0.45, 0.1, m.graphite, 0, 0, 0.25, 32), k.box(0.05, 0.9, 0.05, m.graphite, 0, 0.8, -0.55), k.box(0.6, 0.05, 0.05, m.orange, 0, 1.7, -0.55));
    parts.push(k.part(-0.6, -2.2, 0, scrub));
    const shelf = k.shelf(1.6, 1.9, 0.6, 3);
    for (let lv = 0; lv < 2; lv++) for (let i = 0; i < 5; i++) shelf.add(k.cyl(0.11, 0.42, i % 2 ? m.orange : m.paper, -0.6 + i * 0.3, 0.06 + lv * 0.92, 0, 16));
    parts.push(k.part(2.6, -2.5, 0, shelf));
    const van = new THREE.Group();
    van.add(k.box(2.4, 1.2, 1.2, m.ceramic, 0, 0.3, 0), k.box(0.8, 0.8, 1.16, m.ceramic, 1.55, 0.3, 0), k.box(0.04, 0.4, 1.0, m.glass, 1.96, 0.7, 0), k.box(3.2, 0.12, 1.22, m.orange, 0.4, 0.75, 0));
    for (const x of [-0.7, 1.4]) for (const z of [-0.58, 0.58]) van.add(k.cyl(0.28, 0.2, m.graphite, x, 0.0, z).rotateX(Math.PI / 2));
    parts.push(k.part(1.0, 1.8, 0, van));
    parts.push(k.part(-2.6, 1.8, 0, k.crate(0.8, 0.6, 0.7, 0, 0, 0), k.crate(0.7, 0.5, 0.6, 0.85, 0, 0.1)));
    return { parts };
  },

  Telecommunications(m, k) {
    const parts = [];
    const tower = new THREE.Group();
    const levels = 6;
    for (let i = 0; i < levels; i++) {
      const r = 0.5 - i * 0.06;
      tower.add(k.cyl(r * 0.9, 0.85, m.ceramic, 0, i * 0.85, 0, 6, r));
      tower.add(k.cyl(r + 0.02, 0.06, m.champagne, 0, (i + 1) * 0.85 - 0.03, 0, 6));
    }
    for (let i = 0; i < 3; i++) {
      const a = (i / 3) * Math.PI * 2;
      const panel = k.box(0.22, 0.9, 0.08, m.paper, Math.cos(a) * 0.3, 4.2, Math.sin(a) * 0.3);
      panel.rotation.y = -a + Math.PI / 2;
      tower.add(panel);
    }
    const beacon = k.box(0.14, 0.14, 0.14, new THREE.MeshBasicMaterial({ color: '#FF7900', toneMapped: false }), 0, 5.15, 0);
    tower.add(beacon);
    parts.push(k.part(-2.6, -1.6, 0, tower));
    const dish = new THREE.Group();
    const bowl = new THREE.Mesh(new THREE.SphereGeometry(0.75, 32, 12, 0, Math.PI * 2, 0, Math.PI / 3.2), m.ceramic);
    bowl.castShadow = true;
    bowl.rotation.x = -0.9;
    bowl.position.y = 1.4;
    dish.add(k.cyl(0.08, 1.2, m.graphite), bowl);
    parts.push(k.part(0.0, -2.4, 0.4, dish));
    for (const x of [1.6, 2.7]) {
      const cab = new THREE.Group();
      cab.add(k.box(0.9, 1.9, 0.7, m.ceramic));
      for (let i = 0; i < 4; i++) cab.add(k.box(0.7, 0.05, 0.02, i % 2 ? m.orange : m.graphite, 0, 0.4 + i * 0.35, 0.36));
      parts.push(k.part(x, -2.4, 0, cab));
    }
    const reels = new THREE.Group();
    for (let i = 0; i < 3; i++) {
      const reel = new THREE.Group();
      reel.add(k.cyl(0.55, 0.08, m.kraftIn, 0, 0, 0, 32), k.cyl(0.4, 0.42, m.orange, 0, 0.08, 0, 32), k.cyl(0.55, 0.08, m.kraftIn, 0, 0.5, 0, 32));
      reel.rotation.z = Math.PI / 2;
      reel.position.set(i * 0.7 - 0.7, 0.55, 0);
      reels.add(reel);
    }
    parts.push(k.part(-0.6, 1.8, 0, reels));
    parts.push(k.part(2.2, 1.6, 0, k.crate(1.0, 0.7, 0.9, 0, 0, 0), k.crate(0.8, 0.5, 0.7, 0.2, 0.7, 0)));
    return {
      parts,
      tick(t) {
        beacon.visible = Math.sin(t * 2.2) > 0.2;
        return true;
      }
    };
  },

  Other(m, k) {
    const parts = [];
    const pavilion = new THREE.Group();
    pavilion.add(k.box(3.2, 1.8, 2.2, m.ceramic), k.box(3.4, 0.12, 2.4, m.kraftIn, 0, 1.8, 0), k.box(1.0, 1.1, 0.04, m.glass, -0.6, 0.1, 1.11), k.box(3.42, 0.06, 2.42, m.orange, 0, 1.92, 0));
    parts.push(k.part(-1.6, -1.8, 0, pavilion));
    const shelf = k.shelf(1.6, 1.6, 0.6, 3, m.kraftIn);
    shelf.add(k.crate(0.5, 0.4, 0.4, -0.4, 0.06, 0), k.crate(0.5, 0.4, 0.4, 0.35, 0.83, 0));
    parts.push(k.part(2.4, -2.2, 0, shelf));
    parts.push(k.part(1.6, 1.6, 0, k.crate(1.0, 0.7, 0.9, 0, 0, 0), k.crate(0.9, 0.6, 0.8, 1.0, 0, 0.1), k.crate(0.8, 0.5, 0.7, 0.4, 0.7, 0.05)));
    const planter = new THREE.Group();
    planter.add(k.box(1.6, 0.4, 0.6, m.ceramic));
    for (let i = 0; i < 3; i++) planter.add(k.cyl(0.05, 0.6, m.kraft, -0.5 + i * 0.5, 0.4, 0), new THREE.Mesh(new THREE.IcosahedronGeometry(0.32, 1), m.kraftIn).translateX(-0.5 + i * 0.5).translateY(1.1));
    parts.push(k.part(-2.4, 2.0, 0, planter));
    return { parts };
  }
};

export const CATEGORY_NAMES = Object.keys(BUILDERS);

export function buildCategory(name, mats) {
  const k = kit(mats);
  const build = BUILDERS[name] || BUILDERS.Other;
  const { parts, tick } = build(mats, k);
  const group = new THREE.Group();
  group.name = `category:${name}`;
  for (const p of parts) {
    p.userData.sy = 1;
    group.add(p);
  }
  return { group, parts, tick: tick || null };
}
