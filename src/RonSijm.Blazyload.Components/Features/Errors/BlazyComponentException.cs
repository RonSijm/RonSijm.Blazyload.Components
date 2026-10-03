namespace RonSijm.Blazyload.Components;

public abstract class BlazyComponentException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
