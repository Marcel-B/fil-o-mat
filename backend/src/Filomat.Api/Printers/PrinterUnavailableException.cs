namespace Filomat.Api.Printers;

/// <summary>Der Drucker ist nicht erreichbar oder verweigert die Abfrage; wird wie ein HTTP-Fehler als Abgleichfehler gemeldet.</summary>
public class PrinterUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
