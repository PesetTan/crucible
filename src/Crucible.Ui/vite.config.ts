import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';
export default defineConfig({
 plugins:[svelte()],
 server:{host:'127.0.0.1',port:5173,strictPort:true,proxy:{
  '/api':{target:'http://127.0.0.1:5081',changeOrigin:true,configure(proxy){proxy.on('proxyReq',(upstream,request)=>{if(request.headers.origin==='http://127.0.0.1:5173')upstream.setHeader('Origin','http://127.0.0.1:5081')})}},
  '/branding.css':'http://127.0.0.1:5081'
 }},
 build:{target:'es2022',sourcemap:true}
});
