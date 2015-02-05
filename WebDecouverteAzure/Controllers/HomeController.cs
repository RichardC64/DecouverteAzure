using System.Web.Mvc;

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
    }
}