// A handful of Vulkan handle types share a name with a System type that implicit usings bring in —
// Buffer with System.Buffer, Semaphore with System.Threading.Semaphore. Aliasing them once here keeps
// every file that touches them from repeating the disambiguation, and the backend never means the
// System types.
global using Buffer = Silk.NET.Vulkan.Buffer;
global using Semaphore = Silk.NET.Vulkan.Semaphore;

// Viewport exists in both the rendering seam and Vulkan. The seam's is the one the interface speaks in,
// so it keeps the plain name; the Vulkan dynamic-state struct is reached as VkViewport.
global using Viewport = OpenIGI.Core.Graphics.Viewport;
global using VkViewport = Silk.NET.Vulkan.Viewport;
