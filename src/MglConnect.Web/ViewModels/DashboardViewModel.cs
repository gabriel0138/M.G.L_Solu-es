using System.Collections.Generic;
using MglConnect.Web.Models;

namespace MglConnect.Web.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalClientes { get; set; }
        public int TotalPedidos { get; set; }
        public int EntregasPendentes { get; set; }
        public int EntregasEmTransporte { get; set; }
        public int EntregasEntregues { get; set; }
        public decimal ValorTotalPedidos { get; set; }
        
        // Indicadores Financeiros e de Frota (Módulo 3)
        public decimal CustoTotalCombustivel { get; set; }
        public decimal CustoTotalPedagios { get; set; }
        public decimal CustoTotalFrota { get; set; }
        public decimal MargemLucroLiquida { get; set; }
        public decimal MargemLucroPercentual { get; set; }
        public decimal TotalLitrosCombustivel { get; set; }

        public List<Entrega> EntregasRecentes { get; set; } = new();
        public List<Pedido> PedidosSemEntrega { get; set; } = new();

        // ESG — Pegada de Carbono
        public int TotalEntregasConcluidas { get; set; }
        public double EmissaoCo2TotalKg { get; set; }
    }
}
