// Lets Node load the Worker's modules for unit tests: `cloudflare:sockets` exists only in the Workers runtime.
import { register } from "node:module";

const hooks = `
export async function resolve(specifier, context, next) {
  if (specifier === "cloudflare:sockets") {
    return { url: "data:text/javascript,export function connect() { throw new Error('no sockets in tests'); }", shortCircuit: true };
  }

  return next(specifier, context);
}`;

register("data:text/javascript," + encodeURIComponent(hooks));
