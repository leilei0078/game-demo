import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    rollupOptions: { input: { main: 'index.html', play: 'play.html', workers: 'workers.html', rts: 'rts.html', adventure:'adventure.html', mistwood: 'mistwood.html', trees: 'trees.html', grove: 'grove.html', leafy: 'leafy.html' } },
  },
});
