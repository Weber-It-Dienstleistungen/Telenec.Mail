namespace Telenec.Mail.App;

public partial class App
{
    internal IServiceProvider Services =>
        _host.Services;
}