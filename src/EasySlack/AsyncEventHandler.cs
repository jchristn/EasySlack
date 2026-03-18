namespace EasySlack
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents an asynchronous event handler.
    /// </summary>
    /// <typeparam name="TEventArgs">The event arguments type.</typeparam>
    /// <param name="sender">The event source.</param>
    /// <param name="eventArgs">The event arguments.</param>
    /// <returns>A task representing the asynchronous handler invocation.</returns>
    public delegate Task AsyncEventHandler<TEventArgs>(object sender, TEventArgs eventArgs) where TEventArgs : EventArgs;
}
