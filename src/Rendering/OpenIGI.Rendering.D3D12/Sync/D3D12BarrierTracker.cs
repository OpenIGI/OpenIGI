using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Sync;

/// <summary>How one command uses a resource.</summary>
internal enum D3D12Use
{
    /// <summary>Drawn into as a colour target.</summary>
    RenderTarget,

    /// <summary>Tested and written as the depth target.</summary>
    DepthWrite,

    /// <summary>Tested against as a read-only depth target, and possibly sampled at the same time.</summary>
    DepthRead,

    /// <summary>Sampled or loaded through a shader resource view.</summary>
    Sampled,

    /// <summary>Loaded through an unordered-access view, never stored to.</summary>
    StorageRead,

    /// <summary>Stored to (and possibly loaded) through an unordered-access view.</summary>
    StorageWrite,

    /// <summary>Read as a constant buffer.</summary>
    Constant,

    /// <summary>Read as vertices.</summary>
    Vertex,

    /// <summary>Read as indices.</summary>
    Index,

    /// <summary>Read as indirect arguments.</summary>
    Indirect,

    /// <summary>Copied from.</summary>
    CopySource,

    /// <summary>Copied into, cleared, or a query resolved into.</summary>
    CopyDest,

    /// <summary>Resolved from.</summary>
    ResolveSource,

    /// <summary>Resolved into.</summary>
    ResolveDest,

    /// <summary>Presented: the plain common layout, with nothing after it in the list.</summary>
    Present,

    /// <summary>Handed to Streamline, which records legacy barriers of its own from the plain common layout.</summary>
    External,
}

/// <summary>What a texture needs at the start of a command list, and where the list leaves it.</summary>
/// <param name="Resource">The texture's resource.</param>
/// <param name="Texture">The texture, whose submitted layout this updates.</param>
/// <param name="First">The layout the list's first use of it needs.</param>
/// <param name="FirstSync">The work of that first use.</param>
/// <param name="FirstAccess">Its access.</param>
/// <param name="Last">The layout the list leaves it in.</param>
internal readonly record struct D3D12LayoutHandOff(
    nint Resource, object Texture, BarrierLayout First, BarrierSync FirstSync, BarrierAccess FirstAccess, BarrierLayout Last);

/// <summary>Decides one command list's barriers from what its commands actually use.</summary>
/// <remarks>
/// <para>
/// The RHI says what a command uses and nothing about barriers, as WebGPU does. This turns uses into barriers
/// and only where one is due: a write followed by anything, anything followed by a write, and a texture whose
/// layout the next use cannot work in. Reads after reads, and colour or depth writes after the same, need
/// none. Barriers are gathered and issued together just before the command that needs them.
/// </para>
/// <para>
/// Each command list is its own <c>ExecuteCommandLists</c> call, and the end of one is a full finish and
/// cache flush, so a list starts with nothing outstanding: its first use of a resource is never a hazard.
/// It may still need a layout the resource is not in -- but which layout that is depends on the lists
/// submitted before it, not on those recorded before it, so the first layout is only recorded here as a
/// hand-off and the submission moves the texture into it.
/// </para>
/// <para>
/// A texture is left in the layout of its last use rather than returned anywhere. A depth buffer drawn by three
/// passes stays a depth buffer between them instead of being decompressed and recompressed twice.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12BarrierTracker
{
    private readonly Dictionary<object, State> _states = new(ReferenceEqualityComparer.Instance);
    private readonly List<TextureBarrier> _textures = [];
    private readonly List<BufferBarrier> _buffers = [];
    private readonly List<D3D12LayoutHandOff> _handOffs = [];
    private bool _flushAll;

    /// <summary>Gets or sets a value indicating whether every command waits for everything before it.</summary>
    /// <remarks><c>OPENIGI_D3D12_BARRIERS=full</c>: the bluntest synchronisation there is, for telling a missing barrier from anything else.</remarks>
    public static bool Full { get; set; } =
        string.Equals(Environment.GetEnvironmentVariable("OPENIGI_D3D12_BARRIERS"), "full", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the texture barriers queued for the next flush.</summary>
    internal IReadOnlyList<TextureBarrier> QueuedTextureBarriers => _textures;

    /// <summary>Gets the buffer barriers queued for the next flush.</summary>
    internal IReadOnlyList<BufferBarrier> QueuedBufferBarriers => _buffers;

    /// <summary>Drops the queued barriers without recording them, for a test that has no list.</summary>
    internal void Discard()
    {
        _textures.Clear();
        _buffers.Clear();
    }

    /// <summary>Records a texture's use, queuing whatever barrier it needs.</summary>
    /// <param name="key">The texture.</param>
    /// <param name="resource">Its resource.</param>
    /// <param name="use">How it is used.</param>
    public void UseTexture(object key, nint resource, D3D12Use use)
    {
        Info info = Describe(use);
        if (!_states.TryGetValue(key, out State? state))
        {
            BarrierLayout first = info.Layout;
            _states[key] = new State(resource, isTexture: true) { Layout = first, First = first, FirstSync = info.Sync, FirstAccess = info.Access };
            Remember(_states[key], info);
            return;
        }

        // Targets and the hand-over layouts are exact; anything else stays where it is if that layout allows it.
        bool exact = info.Ordered || info.Layout is BarrierLayout.Common or BarrierLayout.DirectQueueGenericRead;
        if (exact ? state.Layout != info.Layout : !Allows(state.Layout, info.Access))
        {
            BarrierSync syncBefore = state.Pending(out BarrierAccess before);
            _textures.Add(new TextureBarrier
            {
                SyncBefore = syncBefore,
                AccessBefore = before,
                SyncAfter = info.Sync,
                AccessAfter = info.Access,
                LayoutBefore = state.Layout,
                LayoutAfter = info.Layout,
                PResource = (ID3D12Resource*)resource,
                Subresources = new BarrierSubresourceRange { IndexOrFirstMipLevel = uint.MaxValue },
            });
            state.Layout = info.Layout;
            state.Reset();
            Remember(state, info);
            return;
        }

        if (state.Hazard(info))
        {
            BarrierSync syncBefore = state.Pending(out BarrierAccess before);
            _textures.Add(new TextureBarrier
            {
                SyncBefore = syncBefore,
                AccessBefore = before,
                SyncAfter = info.Sync,
                AccessAfter = info.Access,
                LayoutBefore = state.Layout,
                LayoutAfter = state.Layout,
                PResource = (ID3D12Resource*)resource,
                Subresources = new BarrierSubresourceRange { IndexOrFirstMipLevel = uint.MaxValue },
            });
            state.Synchronised(info);
        }

        Remember(state, info);
    }

    /// <summary>Records a buffer's use, queuing whatever barrier it needs.</summary>
    /// <param name="key">The buffer.</param>
    /// <param name="resource">The resource it lives in, which a barrier names whole.</param>
    /// <param name="use">How it is used.</param>
    public void UseBuffer(object key, nint resource, D3D12Use use)
    {
        Info info = Describe(use);
        if (!_states.TryGetValue(key, out State? state))
        {
            state = new State(resource, isTexture: false);
            _states[key] = state;
            Remember(state, info);
            return;
        }

        if (state.Hazard(info))
        {
            BarrierSync syncBefore = state.Pending(out BarrierAccess before);
            _buffers.Add(new BufferBarrier
            {
                SyncBefore = syncBefore,
                AccessBefore = before,
                SyncAfter = info.Sync,
                AccessAfter = info.Access,
                PResource = (ID3D12Resource*)resource,
                Offset = 0,
                Size = ulong.MaxValue,
            });
            state.Synchronised(info);
        }

        Remember(state, info);
    }

    /// <summary>Forgets the outstanding accesses a global barrier covered.</summary>
    /// <remarks>
    /// Layouts are kept: a global barrier changes none. So are colour and depth target writes, which a
    /// global barrier cannot express -- they are flushed by the layout change that moves the texture on.
    /// </remarks>
    public void Covered()
    {
        foreach (State state in _states.Values)
        {
            state.CoveredByGlobal();
        }
    }

    /// <summary>Makes every command after the next flush wait for everything before it.</summary>
    public void RequireFull() => _flushAll = true;

    /// <summary>Issues every queued barrier.</summary>
    /// <param name="list">The list to record into.</param>
    public unsafe void Flush(ID3D12GraphicsCommandList7* list)
    {
        int groups = 0;
        BarrierGroup* batch = stackalloc BarrierGroup[3];
        GlobalBarrier global = new()
        {
            SyncBefore = BarrierSync.All,
            SyncAfter = BarrierSync.All,
            AccessBefore = WriteAccesses,
            AccessAfter = AllAccesses,
        };

        if (_flushAll || Full)
        {
            batch[groups++] = new BarrierGroup { Type = BarrierType.Global, NumBarriers = 1, PGlobalBarriers = &global };
            _flushAll = false;
            Covered();
        }

        if (_textures.Count == 0 && _buffers.Count == 0)
        {
            if (groups > 0)
            {
                list->Barrier((uint)groups, batch);
            }

            return;
        }

        TextureBarrier[] textures = [.. _textures];
        BufferBarrier[] buffers = [.. _buffers];
        _textures.Clear();
        _buffers.Clear();
        fixed (TextureBarrier* firstTexture = textures)
        fixed (BufferBarrier* firstBuffer = buffers)
        {
            if (textures.Length > 0)
            {
                batch[groups++] = new BarrierGroup { Type = BarrierType.Texture, NumBarriers = (uint)textures.Length, PTextureBarriers = firstTexture };
            }

            if (buffers.Length > 0)
            {
                batch[groups++] = new BarrierGroup { Type = BarrierType.Buffer, NumBarriers = (uint)buffers.Length, PBufferBarriers = firstBuffer };
            }

            list->Barrier((uint)groups, batch);
        }
    }

    /// <summary>The layouts the list's textures need first and are left in, for the submission to hand them over.</summary>
    /// <returns>One hand-off per texture the list used.</returns>
    public List<D3D12LayoutHandOff> HandOffs()
    {
        _handOffs.Clear();
        foreach ((object key, State state) in _states)
        {
            if (state.IsTexture)
            {
                _handOffs.Add(new D3D12LayoutHandOff(state.Resource, key, state.First, state.FirstSync, state.FirstAccess, state.Layout));
            }
        }

        return [.. _handOffs];
    }

    /// <summary>Whether a layout lets a command make an access, so no transition is needed.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="access">The access.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <remarks>The compatibility the enhanced-barriers specification gives each layout, for the direct queue.</remarks>
    public static bool Allows(BarrierLayout layout, BarrierAccess access)
    {
        BarrierAccess allowed = layout switch
        {
            BarrierLayout.Common => BarrierAccess.ShaderResource | BarrierAccess.CopyDest | BarrierAccess.CopySource,
            BarrierLayout.DirectQueueCommon => BarrierAccess.ShaderResource | BarrierAccess.CopyDest | BarrierAccess.CopySource | BarrierAccess.UnorderedAccess,
            BarrierLayout.DirectQueueGenericRead => BarrierAccess.ShaderResource | BarrierAccess.CopySource | BarrierAccess.DepthStencilRead | BarrierAccess.ResolveSource,
            BarrierLayout.GenericRead => BarrierAccess.ShaderResource | BarrierAccess.CopySource,
            BarrierLayout.RenderTarget => BarrierAccess.RenderTarget,
            BarrierLayout.DepthStencilWrite => BarrierAccess.DepthStencilRead | BarrierAccess.DepthStencilWrite,
            BarrierLayout.DepthStencilRead => BarrierAccess.DepthStencilRead,
            BarrierLayout.DirectQueueShaderResource or BarrierLayout.ShaderResource => BarrierAccess.ShaderResource,
            BarrierLayout.DirectQueueUnorderedAccess or BarrierLayout.UnorderedAccess => BarrierAccess.UnorderedAccess,
            BarrierLayout.DirectQueueCopySource or BarrierLayout.CopySource => BarrierAccess.CopySource,
            BarrierLayout.DirectQueueCopyDest or BarrierLayout.CopyDest => BarrierAccess.CopyDest,
            BarrierLayout.ResolveSource => BarrierAccess.ResolveSource,
            BarrierLayout.ResolveDest => BarrierAccess.ResolveDest,
            _ => BarrierAccess.NoAccess,
        };

        return access != BarrierAccess.Common && (access & ~allowed) == 0;
    }

    /// <summary>The synchronisation, access and preferred layout of a use.</summary>
    /// <param name="use">The use.</param>
    /// <returns>What it means to Direct3D 12.</returns>
    public static Info Describe(D3D12Use use) => use switch
    {
        D3D12Use.RenderTarget => new(BarrierSync.RenderTarget, BarrierAccess.RenderTarget, BarrierLayout.RenderTarget, Write: true, Ordered: true),
        D3D12Use.DepthWrite => new(BarrierSync.DepthStencil, BarrierAccess.DepthStencilWrite | BarrierAccess.DepthStencilRead, BarrierLayout.DepthStencilWrite, Write: true, Ordered: true),

        // Read-only depth goes into the generic read layout, which a shader may sample in the same pass.
        D3D12Use.DepthRead => new(BarrierSync.DepthStencil, BarrierAccess.DepthStencilRead, BarrierLayout.DirectQueueGenericRead, Write: false, Ordered: false),
        D3D12Use.Sampled => new(BarrierSync.AllShading, BarrierAccess.ShaderResource, BarrierLayout.DirectQueueShaderResource, Write: false, Ordered: false),
        D3D12Use.StorageRead => new(BarrierSync.AllShading, BarrierAccess.UnorderedAccess, BarrierLayout.DirectQueueUnorderedAccess, Write: false, Ordered: false),
        D3D12Use.StorageWrite => new(BarrierSync.AllShading, BarrierAccess.UnorderedAccess, BarrierLayout.DirectQueueUnorderedAccess, Write: true, Ordered: false),
        D3D12Use.Constant => new(BarrierSync.AllShading, BarrierAccess.ConstantBuffer, BarrierLayout.Undefined, Write: false, Ordered: false),
        D3D12Use.Vertex => new(BarrierSync.VertexShading, BarrierAccess.VertexBuffer, BarrierLayout.Undefined, Write: false, Ordered: false),
        D3D12Use.Index => new(BarrierSync.IndexInput, BarrierAccess.IndexBuffer, BarrierLayout.Undefined, Write: false, Ordered: false),
        D3D12Use.Indirect => new(BarrierSync.ExecuteIndirect, BarrierAccess.IndirectArgument, BarrierLayout.Undefined, Write: false, Ordered: false),
        D3D12Use.CopySource => new(BarrierSync.Copy, BarrierAccess.CopySource, BarrierLayout.DirectQueueCopySource, Write: false, Ordered: false),
        D3D12Use.CopyDest => new(BarrierSync.Copy, BarrierAccess.CopyDest, BarrierLayout.DirectQueueCopyDest, Write: true, Ordered: false),
        D3D12Use.ResolveSource => new(BarrierSync.Resolve, BarrierAccess.ResolveSource, BarrierLayout.ResolveSource, Write: false, Ordered: false),
        D3D12Use.ResolveDest => new(BarrierSync.Resolve, BarrierAccess.ResolveDest, BarrierLayout.ResolveDest, Write: true, Ordered: false),
        D3D12Use.Present => new(BarrierSync.None, BarrierAccess.NoAccess, BarrierLayout.Common, Write: false, Ordered: false),
        D3D12Use.External => new(BarrierSync.All, BarrierAccess.Common, BarrierLayout.Common, Write: false, Ordered: false),
        _ => throw new ArgumentOutOfRangeException(nameof(use), use, null),
    };

    private static void Remember(State state, Info info)
    {
        if (info.Write)
        {
            state.WriteSync |= info.Sync;
            state.WriteAccess |= info.Access;
            state.Ordered = info.Ordered && (state.Ordered || state.ReadSync == BarrierSync.None);
            state.VisibleSync = BarrierSync.None;
            state.VisibleAccess = BarrierAccess.NoAccess;
        }
        else if (info.Access != BarrierAccess.NoAccess)
        {
            state.ReadSync |= info.Sync;
            state.ReadAccess |= info.Access;
        }
    }

    /// <summary>Every write a global barrier can flush: none tied to a layout, as target writes are.</summary>
    private const BarrierAccess WriteAccesses = BarrierAccess.UnorderedAccess
        | BarrierAccess.CopyDest
        | BarrierAccess.ResolveDest
        | BarrierAccess.RaytracingAccelerationStructureWrite;

    /// <summary>Every access a global barrier can make visible to: again none tied to a layout.</summary>
    private const BarrierAccess AllAccesses = BarrierAccess.VertexBuffer
        | BarrierAccess.ConstantBuffer
        | BarrierAccess.IndexBuffer
        | BarrierAccess.UnorderedAccess
        | BarrierAccess.ShaderResource
        | BarrierAccess.IndirectArgument
        | BarrierAccess.CopySource
        | BarrierAccess.CopyDest
        | BarrierAccess.ResolveSource
        | BarrierAccess.ResolveDest
        | BarrierAccess.RaytracingAccelerationStructureRead
        | BarrierAccess.RaytracingAccelerationStructureWrite;

    /// <summary>A use, as Direct3D 12 sees it.</summary>
    /// <param name="Sync">The work that makes it.</param>
    /// <param name="Access">The access.</param>
    /// <param name="Layout">The layout a texture is moved into for it, where its layout does not already allow it.</param>
    /// <param name="Write">Whether it writes.</param>
    /// <param name="Ordered">Whether writes like it are ordered among themselves with no barrier: colour and depth targets.</param>
    internal readonly record struct Info(BarrierSync Sync, BarrierAccess Access, BarrierLayout Layout, bool Write, bool Ordered);

    private sealed class State(nint resource, bool isTexture)
    {
        public nint Resource { get; } = resource;

        public bool IsTexture { get; } = isTexture;

        public BarrierLayout First { get; set; }

        public BarrierSync FirstSync { get; set; }

        public BarrierAccess FirstAccess { get; set; }

        public BarrierLayout Layout { get; set; }

        // Writes not yet made visible to anything, and the reads since the last barrier.
        public BarrierSync WriteSync { get; set; }

        public BarrierAccess WriteAccess { get; set; }

        public BarrierSync ReadSync { get; set; }

        public BarrierAccess ReadAccess { get; set; }

        // What the outstanding writes have already been made visible to by a barrier.
        public BarrierSync VisibleSync { get; set; }

        public BarrierAccess VisibleAccess { get; set; }

        // Whether the outstanding writes are all colour or depth target writes, which need no barrier between them.
        public bool Ordered { get; set; }

        /// <summary>Whether a use must wait for what came before it.</summary>
        public bool Hazard(Info info)
        {
            bool written = WriteSync != BarrierSync.None;
            if (info.Write)
            {
                // Target after target needs nothing; anything else after a write or a read does.
                if (info.Ordered && Ordered && ReadSync == BarrierSync.None)
                {
                    return false;
                }

                return written || ReadSync != BarrierSync.None;
            }

            // A read after a write, unless a barrier already made the write visible to this kind of read.
            return written && ((info.Sync & ~VisibleSync) != 0 || (info.Access & ~VisibleAccess) != 0);
        }

        /// <summary>What a barrier waits for: the outstanding writes and reads, and their accesses.</summary>
        /// <remarks>
        /// The reads are named too, not only waited for: a texture barrier with no access before it must have no
        /// work before it either, or come from the undefined layout.
        /// </remarks>
        public BarrierSync Pending(out BarrierAccess access)
        {
            BarrierSync sync = WriteSync | ReadSync;
            access = WriteAccess | ReadAccess;
            if (sync == BarrierSync.None || access == default)
            {
                access = BarrierAccess.NoAccess;
                return BarrierSync.None;
            }

            return sync;
        }

        /// <summary>After a barrier from everything outstanding to a use.</summary>
        public void Synchronised(Info info)
        {
            if (info.Write)
            {
                Reset();
                return;
            }

            // The writes stay outstanding for any later write, but are visible to this kind of read now.
            VisibleSync |= info.Sync;
            VisibleAccess |= info.Access;
            ReadSync = BarrierSync.None;
            ReadAccess = default;
        }

        public void CoveredByGlobal()
        {
            const BarrierAccess TargetWrites = BarrierAccess.RenderTarget | BarrierAccess.DepthStencilWrite | BarrierAccess.DepthStencilRead;
            BarrierAccess kept = WriteAccess & TargetWrites;
            BarrierSync keptSync = kept == 0 ? BarrierSync.None : WriteSync & (BarrierSync.RenderTarget | BarrierSync.DepthStencil);
            bool ordered = Ordered;
            Reset();
            WriteAccess = kept;
            WriteSync = keptSync;
            Ordered = kept != 0 && ordered;
        }

        // Accesses are gathered by OR-ing bits into these, so "none" is the empty set, not NO_ACCESS: that is a
        // bit of its own, which no other access may be combined with.
        public void Reset()
        {
            WriteSync = BarrierSync.None;
            WriteAccess = default;
            ReadSync = BarrierSync.None;
            ReadAccess = default;
            VisibleSync = BarrierSync.None;
            VisibleAccess = default;
            Ordered = false;
        }
    }
}
