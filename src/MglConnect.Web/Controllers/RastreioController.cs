using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MglConnect.Web.Services;
using MglConnect.Web.ViewModels;

namespace MglConnect.Web.Controllers
{
    public class RastreioController : Controller
    {
        private readonly ITrackingService _trackingService;

        public RastreioController(ITrackingService trackingService)
        {
            _trackingService = trackingService;
        }

        [HttpGet]
        [Route("Rastreio")]
        [Route("Rastreio/{codigo}")]
        public async Task<IActionResult> Index(string? codigo)
        {
            var viewModel = new RastreioViewModel
            {
                CodigoBuscado = codigo?.Trim().ToUpper()
            };

            if (!string.IsNullOrWhiteSpace(codigo))
            {
                viewModel.Pesquisado = true;
                var entrega = await _trackingService.GetTrackingDetailsAsync(codigo);
                if (entrega == null)
                {
                    viewModel.MensagemErro = $"Nenhum pacote encontrado com o código de rastreamento \"{codigo}\". Verifique o código digitado e tente novamente.";
                }
                else
                {
                    viewModel.Entrega = entrega;
                }
            }

            return View(viewModel);
        }
    }
}
