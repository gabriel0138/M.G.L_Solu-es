using System;
using System.Threading.Tasks;
using MglConnect.Web.Models;

namespace MglConnect.Web.Services
{
    public interface IPodPdfService
    {
        /// <summary>
        /// Gera o PDF do Comprovante Digital de Entrega (POD) com assinatura, coordenadas GPS e dados fiscais.
        /// Retorna os bytes do PDF pronto para download ou envio por e-mail.
        /// </summary>
        Task<byte[]> GerarPodPdfAsync(Entrega entrega);
    }
}
