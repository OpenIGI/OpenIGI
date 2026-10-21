using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using OpenIGI.Core.Diagnostics;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12;

/// <summary>What DRED recorded when the device was removed, written to the log.</summary>
/// <remarks>
/// The counterpart of the Vulkan backend's checkpoint markers. Every command list is labelled with the
/// encoder that recorded it, and the breadcrumbs say how far into each one the GPU got: the list that was
/// running, its last completed operation and the one after it. Where page-fault reporting was on, the
/// allocations near the faulting address say which resource it was.
/// </remarks>
internal static unsafe class D3D12DeviceRemoved
{
    private const string LogCategory = "D3D12";

    /// <summary>Writes what DRED has to the log.</summary>
    /// <param name="device">The removed device.</param>
    /// <param name="logger">Where to write.</param>
    /// <param name="reason">The removal reason.</param>
    public static void Report(ID3D12Device10* device, IOpenIgiLogger logger, int reason)
    {
        StringBuilder text = new($"Device removed (0x{reason:X8}).");

        ID3D12DeviceRemovedExtendedData1* dred = null;
        Guid iid = ID3D12DeviceRemovedExtendedData1.Guid;
        if (device->QueryInterface(&iid, (void**)&dred) < 0 || dred is null)
        {
            logger.Error(LogCategory, text.Append(" DRED is not available.").ToString());
            return;
        }

        try
        {
            DredAutoBreadcrumbsOutput1 breadcrumbs = default;
            if (dred->GetAutoBreadcrumbsOutput1(&breadcrumbs) >= 0)
            {
                for (AutoBreadcrumbNode1* node = breadcrumbs.PHeadAutoBreadcrumbNode; node is not null; node = node->PNext)
                {
                    uint completed = node->PLastBreadcrumbValue is null ? 0 : *node->PLastBreadcrumbValue;
                    if (completed >= node->BreadcrumbCount)
                    {
                        continue;
                    }

                    string list = node->PCommandListDebugNameW is null ? "?" : new string(node->PCommandListDebugNameW);
                    text.Append(CultureInfo.InvariantCulture, $" Unfinished: '{list}' at {completed} of {node->BreadcrumbCount}");
                    if (completed < node->BreadcrumbCount)
                    {
                        text.Append(CultureInfo.InvariantCulture, $", next {node->PCommandHistory[completed]}");
                    }

                    text.Append('.');
                }
            }

            DredPageFaultOutput1 fault = default;
            if (dred->GetPageFaultAllocationOutput1(&fault) >= 0 && fault.PageFaultVA != 0)
            {
                text.Append(CultureInfo.InvariantCulture, $" Page fault at 0x{fault.PageFaultVA:X}.");
                for (DredAllocationNode1* node = fault.PHeadExistingAllocationNode; node is not null; node = node->PNext)
                {
                    text.Append(CultureInfo.InvariantCulture, $" Live nearby: {Name(node)}.");
                }

                for (DredAllocationNode1* node = fault.PHeadRecentFreedAllocationNode; node is not null; node = node->PNext)
                {
                    text.Append(CultureInfo.InvariantCulture, $" Freed recently: {Name(node)}.");
                }
            }
        }
        finally
        {
            dred->Release();
        }

        logger.Error(LogCategory, text.ToString());
    }

    private static string Name(DredAllocationNode1* node) =>
        node->ObjectNameW is not null
            ? new string(node->ObjectNameW)
            : node->ObjectNameA is not null ? Marshal.PtrToStringAnsi((nint)node->ObjectNameA) ?? "?" : "?";
}
