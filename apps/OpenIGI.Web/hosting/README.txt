OpenIGI web build - hosting on cPanel (Apache)
================================================

1. Upload
   Extract this zip into the folder you want to serve it from, for example public_html/ or a subfolder
   such as public_html/igi/. Every path in the page is relative, so any folder works.
   Keep both .htaccess files (the root one and _framework/.htaccess): they give every file its MIME type,
   serve the pre-compressed .br / .gz files and set how long browsers keep each file.
   (In cPanel's File Manager, turn on "Show Hidden Files" to see them.)

2. HTTPS is required
   Browsers only allow WebGPU on a secure page, so open the site over https://. cPanel's
   "SSL/TLS Status" (AutoSSL) can issue a free certificate.

3. Browser
   A browser with WebGPU: current Chrome or Edge on Windows/macOS work.

4. Game data
   The game files are NOT in this zip. When the page opens, it asks the player to pick their own
   Project I.G.I. installation folder; nothing is uploaded, the files are read in the browser.

   Optional: serve the data from your own host instead, with ?data=<url>:
     - upload the installation folder, e.g. to public_html/igi-data/
     - put a manifest.json in that folder: a JSON array of { "path": "<relative/path>", "size": <bytes> },
       one entry per file (the local tools/serve-web.py builds exactly this list on request)
     - open  https://your-site/igi/?data=/igi-data/
   Only do this with game data you are entitled to host.

5. Updating
   Upload the new build over the old one; old files under _framework can be deleted. The page and its
   scripts are sent with Cache-Control: no-cache, so visitors pick up a new build on their next load; the
   runtime files under _framework have content hashes in their names and are cached for good.
   Behind Cloudflare, keep "Browser Cache TTL" on "Respect Existing Headers".
