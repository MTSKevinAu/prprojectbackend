using medxbackend.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace medxbackend.Controllers
{
    public class HomeController : Controller
    {
        /// <summary>显示网站首页。</summary>
        public IActionResult Index()
        {
            return View();
        }

        /// <summary>显示隐私说明页面。</summary>
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>显示错误页面并提供当前请求标识。</summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
