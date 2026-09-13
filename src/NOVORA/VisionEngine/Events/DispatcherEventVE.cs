namespace NOVORA.VisionEngine.Events;

public sealed class DispatcherEventVE
{
    public event EventHandler<MessageEventVE>? EventRaisedVE;

    public void PublishVE(MessageEventVE message)
    {
        ArgumentNullException.ThrowIfNull(message);
        EventHandler<MessageEventVE>? handler = EventRaisedVE;
        if (handler is null) return;

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<MessageEventVE>)subscriber)(this, message);
            }
            catch
            {
                // Un observador nunca debe romper el productor del evento.
            }
        }
    }
}
