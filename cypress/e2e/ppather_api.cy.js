const mapRouteQuery = {
  uimap1: 1451,
  x1: 46.8,
  y1: 54.2,
  uimap2: 1451,
  x2: 51.2,
  y2: 38.9,
};

const worldRouteQuery = {
  x1: -896,
  y1: -3770,
  z1: 11,
  x2: -441,
  y2: -2596,
  z2: 96,
  mapid: 1,
  reverse: false,
};

const longWorldRouteQuery = {
  x1: -896,
  y1: -3770,
  z1: 11,
  x2: 1500,
  y2: -1000,
  z2: 50,
  mapid: 1,
  reverse: false,
};

const worldRoute2Query = {
  x1: -896,
  y1: -3770,
  z1: 11,
  x2: -441,
  y2: -2596,
  z2: 96,
  uimap: 1413,
  startindoors: false,
};

const mapToWorldQuery = {
  x1: 30,
  y1: 73,
  z1: 0,
  x2: 42,
  y2: 59,
  z2: 0,
  uimap: 1426,
};

const mapPathPayload = {
  uiMapId: 1426,
  path: [
    { x: 42.30905, y: 59.866, z: 0 },
    { x: 42.802, y: 59.483, z: 0 },
    { x: 43.40704, y: 59.327, z: 0 },
  ],
};

const worldPathPayload = {
  mapId: 0,
  path: [
    { x: -6220.71, y: 347.44037, z: 384.21396 },
    { x: -6214.267, y: 372.179, z: 385.83997 },
    { x: -6207.5337, y: 393.90826, z: 387.28632 },
  ],
};

const drawLinesPayload = [
  {
    name: 'test-line',
    spots: [
      { x: 40.0, y: 60.0, z: 0 },
      { x: 41.0, y: 60.5, z: 0 },
      { x: 42.0, y: 61.0, z: 0 },
    ],
    colour: 0xff0000,
    mapId: 1426,
  },
];

const drawSpherePayload = {
  name: 'player-marker',
  spot: { x: 45.0, y: 55.0, z: 0 },
  colour: 0x00ff00,
  mapId: 1426,
};

const expectVectorArray = (body) => {
  expect(body).to.be.an('array');
  if (body.length > 0) {
    expect(body[0]).to.have.all.keys('x', 'y', 'z');
  }
};

describe('PPather API', () => {
  it('confirms the service is ready', () => {
    cy.request('/api/PPather/SelfTest').then(({ status, body }) => {
      expect(status).to.eq(200);
      expect(body).to.be.a('boolean');
      expect(body).to.eq(true);
    });
  });

  it('calculates a minimap route', () => {
    cy.request({ url: '/api/PPather/MapRoute', qs: mapRouteQuery }).then(({ status, body }) => {
      expect(status).to.eq(200);
      expectVectorArray(body);
    });
  });

  it('calculates a world route', () => {
    cy.request({ url: '/api/PPather/WorldRoute', qs: worldRouteQuery }).then(({ status, body }) => {
      expect(status).to.eq(200);
      expectVectorArray(body);
    });
  });

  it('calculates a long world route', () => {
    cy.request({ url: '/api/PPather/WorldRoute', qs: longWorldRouteQuery }).then(({ status, body }) => {
      expect(status).to.eq(200);
      expectVectorArray(body);
      if (body.length > 0) {
        expect(body.length).to.be.greaterThan(10);
      }
    });
  });

  it('calculates a world route using map elevation', () => {
    cy.request({ url: '/api/PPather/WorldRoute2', qs: worldRoute2Query }).then(({ status, body }) => {
      expect(status).to.eq(200);
      expectVectorArray(body);
    });
  });

  it('maps minimap coordinates to world route', () => {
    cy.request({ url: '/api/PPather/MapToWorldRoute', qs: mapToWorldQuery }).then(({ status, body }) => {
      expect(status).to.eq(200);
      expectVectorArray(body);
    });
  });

  it('draws lines', () => {
    cy.request('POST', '/api/PPather/Drawlines', drawLinesPayload).then(({ status }) => {
      expect(status).to.eq(202);
    });
  });

  it('draws a sphere', () => {
    cy.request('POST', '/api/PPather/DrawSphere', drawSpherePayload).then(({ status }) => {
      expect(status).to.eq(202);
    });
  });

  it('draws a canned test path', () => {
    cy.request('POST', '/api/PPather/DrawPathTest').then(({ status }) => {
      expect(status).to.eq(202);
    });
  });

  it('draws a world path', () => {
    cy.request('POST', '/api/PPather/DrawWorldPath', worldPathPayload).then(({ status }) => {
      expect(status).to.eq(202);
    });
  });

  it('draws a map path', () => {
    cy.request('POST', '/api/PPather/DrawMapPath', mapPathPayload).then(({ status }) => {
      expect(status).to.eq(202);
    });
  });

  it('resets the service', () => {
    cy.request('POST', '/api/PPather/Reset').then(({ status }) => {
      expect(status).to.eq(202);
    });
  });
});
