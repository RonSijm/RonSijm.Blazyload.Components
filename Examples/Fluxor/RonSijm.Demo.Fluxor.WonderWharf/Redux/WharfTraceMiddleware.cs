using Fluxor;

namespace RonSijm.Demo.Fluxor.WonderWharf.Redux;

public sealed class WharfTraceMiddleware : Middleware
{
    public int LoadActionsSeen { get; private set; }

    public override void BeforeDispatch(object action)
    {
        if (action is LoadWharfEvents)
        {
            LoadActionsSeen++;
        }
    }
}
