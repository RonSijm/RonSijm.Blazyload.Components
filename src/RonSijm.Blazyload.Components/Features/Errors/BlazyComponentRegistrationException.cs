namespace RonSijm.Blazyload.Components;

public sealed class BlazyComponentRegistrationException(string message, Exception? innerException = null) : BlazyComponentException(message, innerException);
