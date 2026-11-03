using MglConnect.Web.Services;

namespace MglConnect.Web.ViewModels
{
    public class RastreioViewModel
    {
        public string? CodigoBuscado { get; set; }
        public PublicTrackingDetails? Entrega { get; set; }
        public bool Pesquisado { get; set; }
        public string? MensagemErro { get; set; }
    }
}
