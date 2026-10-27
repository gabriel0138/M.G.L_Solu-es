using MglConnect.Web.Models;

namespace MglConnect.Web.ViewModels
{
    public class RastreioViewModel
    {
        public string? CodigoBuscado { get; set; }
        public Entrega? Entrega { get; set; }
        public bool Pesquisado { get; set; }
        public string? MensagemErro { get; set; }
    }
}
