using Filomat.Api.Domain;

namespace Filomat.Api.Printers;

/// <summary>
/// Verteilt Änderungen an die offenen Verbindungen von <c>/api/printers/events</c>. Jede Verbindung merkt sich nur,
/// <em>dass</em> sich etwas geändert hat; was sie schickt, baut sie selbst (in der Sprache ihres Clients). So staut
/// sich bei einem langsamen Client nichts an.
/// </summary>
public class PrinterEvents
{
    private readonly Lock _gate = new();
    private readonly HashSet<Listener> _listeners = [];
    private TaskCompletionSource _anyListener = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int ListenerCount
    {
        get
        {
            lock (_gate) return _listeners.Count;
        }
    }

    /// <summary>Zustand eines Druckers neu (Abfrage, Push vom Drucker, Leser).</summary>
    public void StatusChanged() => Publish(status: true, data: false);

    /// <summary>Daten neu (Abgleich, Änderung über die API): Clients laden Drucker, Spulen usw. neu.</summary>
    public void DataChanged() => Publish(status: true, data: true);

    private void Publish(bool status, bool data)
    {
        lock (_gate)
            foreach (var listener in _listeners) listener.Mark(status, data);
    }

    public Listener Subscribe()
    {
        var listener = new Listener(this);
        lock (_gate)
        {
            _listeners.Add(listener);
            _anyListener.TrySetResult();
        }
        return listener;
    }

    /// <summary>Kehrt zurück, sobald jemand zuhört.</summary>
    public Task WaitForListenerAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return _anyListener.Task.WaitAsync(cancellationToken);
    }

    private void Remove(Listener listener)
    {
        lock (_gate)
        {
            if (_listeners.Remove(listener) && _listeners.Count == 0)
                _anyListener = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public sealed class Listener(PrinterEvents events) : IDisposable
    {
        private readonly SemaphoreSlim _signal = new(0, 1);
        private bool _status;
        private bool _data;

        internal void Mark(bool status, bool data)
        {
            lock (_signal)
            {
                _status |= status;
                _data |= data;
                if (_signal.CurrentCount == 0) _signal.Release();
            }
        }

        /// <summary>Wartet auf eine Änderung; <c>(false, false)</c>, wenn in <paramref name="timeout"/> keine kam.</summary>
        public async Task<(bool Status, bool Data)> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(timeout, cancellationToken);
            lock (_signal)
            {
                var result = (_status, _data);
                _status = _data = false;
                return result;
            }
        }

        public void Dispose() => events.Remove(this);
    }
}

/// <summary>Drucker, die ihren Zustand selbst melden (Moonraker über WebSocket, Bambu über MQTT).</summary>
public interface IPrinterPush
{
    /// <summary>
    /// Hält eine Verbindung zum Drucker und ruft <paramref name="changed"/> bei jeder Meldung. Kehrt zurück oder wirft,
    /// wenn die Verbindung abreißt; der Aufrufer verbindet dann neu.
    /// </summary>
    Task WatchAsync(Printer printer, Action changed, CancellationToken cancellationToken);
}
