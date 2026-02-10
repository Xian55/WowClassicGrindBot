const { defineConfig } = require('cypress');

const baseUrl = process.env.CYPRESS_BASE_URL || 'http://localhost:5001';

module.exports = defineConfig({
  e2e: {
    baseUrl,
    specPattern: 'cypress/e2e/**/*.cy.{js,jsx,ts,tsx}',
    supportFile: false,
  },
  video: false,
});
