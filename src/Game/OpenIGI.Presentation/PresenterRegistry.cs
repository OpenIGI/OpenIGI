using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>Makes one presenter from what its request carries.</summary>
/// <typeparam name="TRequest">What the presenter is made from.</typeparam>
/// <typeparam name="TPresenter">The presenter's interface.</typeparam>
/// <param name="request">The request.</param>
/// <returns>The presenter, or <see langword="null"/> when there is nothing for it to draw.</returns>
public delegate TPresenter? PresenterFactory<TRequest, out TPresenter>(in TRequest request)
    where TPresenter : class;

/// <summary>What a presenter for one task of the level is made from.</summary>
/// <param name="Device">The graphics device to upload through and draw with.</param>
/// <param name="Resources">The level's resources.</param>
/// <param name="LevelRoot">The level's task tree.</param>
/// <param name="Logger">Where load diagnostics go.</param>
public readonly record struct TaskPresenterRequest(
    IGraphicsDevice Device,
    ResourceRegistry Resources,
    GameTask LevelRoot,
    IOpenIgiLogger Logger);

/// <summary>A presenter that draws one task of the level, made for it by a <see cref="PresenterRegistry.ForTask{TTask}"/> registration.</summary>
public interface ITaskPresenter : IDisposable
{
    /// <summary>Draws the task in the world pass.</summary>
    /// <param name="camera">The camera the frame is drawn from.</param>
    /// <param name="projection">The frame's projection.</param>
    /// <param name="blend">How far the frame is between the last tick and the next, 0 to 1.</param>
    void Draw(in Camera3D camera, in Matrix4f projection, float blend);
}

/// <summary>The presenters a title draws with, keyed by what each is made from and the interface it is used through.</summary>
/// <remarks>
/// <para>
/// A title registers what it can draw and is silent about the rest: the host asks for a presenter and draws nothing where
/// none is registered, or where the factory answers <see langword="null"/>. The same interface may be made from two
/// requests -- a ground from a terrain surface for one title, from the level itself for the other -- and is then two
/// registrations.
/// </para>
/// <para>
/// A mod replaces one of a title's presenters with <see cref="Replace{TRequest, TPresenter}"/>, and draws a task type of
/// its own with <see cref="ForTask{TTask}"/>, without subclassing the title's provider.
/// </para>
/// </remarks>
public sealed class PresenterRegistry
{
    private readonly Dictionary<(Type Request, Type Presenter), Delegate> _factories = [];
    private readonly Dictionary<Type, Func<GameTask, TaskPresenterRequest, ITaskPresenter?>> _taskFactories = [];

    /// <summary>Gets how many task types have a presenter of their own.</summary>
    public int TaskTypeCount => _taskFactories.Count;

    /// <summary>Registers a presenter.</summary>
    /// <typeparam name="TRequest">What the presenter is made from.</typeparam>
    /// <typeparam name="TPresenter">The interface it is used through.</typeparam>
    /// <param name="factory">What makes it.</param>
    /// <returns>This registry.</returns>
    /// <exception cref="InvalidOperationException">One is already registered for the pair; <see cref="Replace{TRequest, TPresenter}"/> says so on purpose.</exception>
    public PresenterRegistry Add<TRequest, TPresenter>(PresenterFactory<TRequest, TPresenter> factory)
        where TPresenter : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_factories.TryAdd((typeof(TRequest), typeof(TPresenter)), factory))
        {
            throw new InvalidOperationException(
                $"A {typeof(TPresenter).Name} made from a {typeof(TRequest).Name} is already registered.");
        }

        return this;
    }

    /// <summary>Registers a presenter in place of whatever was registered for the pair, if anything.</summary>
    /// <typeparam name="TRequest">What the presenter is made from.</typeparam>
    /// <typeparam name="TPresenter">The interface it is used through.</typeparam>
    /// <param name="factory">What makes it.</param>
    /// <returns>This registry.</returns>
    public PresenterRegistry Replace<TRequest, TPresenter>(PresenterFactory<TRequest, TPresenter> factory)
        where TPresenter : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factories[(typeof(TRequest), typeof(TPresenter))] = factory;
        return this;
    }

    /// <summary>Gets whether a presenter is registered for the pair.</summary>
    /// <typeparam name="TRequest">What the presenter is made from.</typeparam>
    /// <typeparam name="TPresenter">The interface it is used through.</typeparam>
    /// <returns><see langword="true"/> when one is.</returns>
    public bool Has<TRequest, TPresenter>()
        where TPresenter : class =>
        _factories.ContainsKey((typeof(TRequest), typeof(TPresenter)));

    /// <summary>Makes a presenter.</summary>
    /// <typeparam name="TRequest">What the presenter is made from.</typeparam>
    /// <typeparam name="TPresenter">The interface it is used through.</typeparam>
    /// <param name="request">The request.</param>
    /// <returns>The presenter, or <see langword="null"/> when none is registered or the factory has nothing to draw.</returns>
    public TPresenter? Create<TRequest, TPresenter>(in TRequest request)
        where TPresenter : class =>
        _factories.TryGetValue((typeof(TRequest), typeof(TPresenter)), out Delegate? factory)
            ? ((PresenterFactory<TRequest, TPresenter>)factory)(in request)
            : null;

    /// <summary>Registers what draws every task of one type the level holds.</summary>
    /// <typeparam name="TTask">The task type; a subclass with none of its own is drawn by this.</typeparam>
    /// <param name="factory">What makes the presenter for one task, or answers <see langword="null"/> to draw it with nothing.</param>
    /// <returns>This registry.</returns>
    /// <remarks>A later registration for the same type takes its place.</remarks>
    public PresenterRegistry ForTask<TTask>(Func<TTask, TaskPresenterRequest, ITaskPresenter?> factory)
        where TTask : GameTask
    {
        ArgumentNullException.ThrowIfNull(factory);
        _taskFactories[typeof(TTask)] = (task, request) => factory((TTask)task, request);
        return this;
    }

    /// <summary>Makes the presenter registered for a task's type, or for the nearest of its base types that has one.</summary>
    /// <param name="task">The task.</param>
    /// <param name="request">What the presenter is made from.</param>
    /// <returns>The presenter, or <see langword="null"/> when the task's type has none.</returns>
    public ITaskPresenter? CreateForTask(GameTask task, in TaskPresenterRequest request)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_taskFactories.Count == 0)
        {
            return null;
        }

        for (Type? type = task.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            if (_taskFactories.TryGetValue(type, out Func<GameTask, TaskPresenterRequest, ITaskPresenter?>? factory))
            {
                return factory(task, request);
            }
        }

        return null;
    }
}
