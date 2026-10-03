using Android.Runtime;

namespace Balancia.Android;

[Application]
public sealed class MainApplication : Application
{
    public MainApplication(nint handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        ApplicationLogging.Configure(this);
    }
}
