using System.Web.Mvc;
using WebDecouverteAzure.Models;

namespace WebDecouverteAzure.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult Parametrage()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Parametrage(ParametrageModel model)
        {
            if (model.Duration >= 5)
            {
               
            }
            return View();
        }
    }
}