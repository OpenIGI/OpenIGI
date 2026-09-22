namespace OpenIGI.Tools.WebGpuBind;

/// <summary>Something in the headers, or in what was committed from them, is not as expected.</summary>
public sealed class WebGpuBindException : Exception
{
    /// <summary>Initialises a new instance of the <see cref="WebGpuBindException"/> class.</summary>
    public WebGpuBindException()
    {
    }

    /// <summary>Initialises a new instance of the <see cref="WebGpuBindException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    public WebGpuBindException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new instance of the <see cref="WebGpuBindException"/> class.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">What caused it.</param>
    public WebGpuBindException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
