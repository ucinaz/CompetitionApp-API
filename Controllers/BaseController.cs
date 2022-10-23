using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Dapper;
using CompetitionApp.Models;
using System.Security.Claims;
using Microsoft.Extensions.Hosting;
using System.Net.Mail;
using SmtpClient = System.Net.Mail.SmtpClient;

namespace CompetitionApp.Controllers
{
    [Route("api/")]
    [ApiController]

    public class BaseController : ControllerBase
    {
        public readonly IConfiguration _configuration;
        public readonly IHostEnvironment _enviroment;
        public readonly SqlConnection conn;
        public readonly IHttpContextAccessor _httpContextAccessor;

        public BaseController(IConfiguration configuration, IHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
        {
            this._configuration = configuration;
            this._enviroment = environment;
            this._httpContextAccessor = httpContextAccessor;
            conn = new SqlConnection(GetConnection());
        }

        private string GetConnection()
        {
            try
            {
                return _configuration["ConnectionStrings:CompetitionAppCnn"];
            }
            catch (Exception Ex1)
            {
                return "Yapılandırma ayarları okuma hatası. (" + Ex1.Message + ")";
            }
        }

        [NonAction]
        public int GetActiveUserId()
        {
            var CValue = GetClaimValueByName("UserId", "-1", true);
            return Int32.Parse(CValue);
        }

        [NonAction]
        public UserInfo GetActiveUser()
        {
            string SqlStr = "";
            SqlStr += "SELECT * from user_info where user_id = @user_id";
            return conn.QueryFirstOrDefault<UserInfo>(SqlStr, new { user_id = GetActiveUserId() });
        }

        [NonAction]
        public string GetClaimValueByName(string UserId, string VarsayilanDeger, bool HataUret)
        {
            var ClaimValue = _httpContextAccessor.HttpContext.User.FindFirstValue(UserId);
            if (ClaimValue == null)
            {
                if (HataUret) throw new System.Exception("Couldn't get active user info."); else return VarsayilanDeger;
            }
            else
            {
                return ClaimValue;
            }
        }

        [NonAction]
        public ActionResult AktivasyonEPostaGonder(int aUserId, bool hataUret = false)
        {
            serviceResponse<bool> apiResponse = new();
            try
            {
                string portno = _configuration["MailSettings:Port"];
                if (string.IsNullOrEmpty(portno)) throw new Exception("E-posta konfigurasyon bilgileri okunamadi.");

                var AUserInfo = conn.QueryFirst<UserInfo>("select * from user_info where user_id = @userId", new { userId = aUserId });
                SmtpClient smtpClient = new SmtpClient("mail.example.com", Int32.Parse(portno));

                smtpClient.Credentials = new System.Net.NetworkCredential("info@example.com", "password");
                // smtpClient.UseDefaultCredentials = true; // uncomment if you don't want to use the network credentials
                smtpClient.DeliveryMethod = SmtpDeliveryMethod.Network;
                smtpClient.EnableSsl = false;
                MailMessage mail = new MailMessage();

                //Setting From , To and CC

                mail.From = new MailAddress("info@example.com", "CompetitionApp");
                mail.To.Add(new MailAddress(AUserInfo.email));
                mail.Subject = "Please activate your account." + AUserInfo.username;
                //mail.Body = "Welcome to the competition. Please activate your account. Good Luck <br> ->" + AUserInfo.username + " https://www.example.com/#/activation?guid=" + AUserInfo.activationGUID + " ";
                string body = "<!DOCTYPE html><html><body>" + "<div> Hey " + AUserInfo.username + ",";
                string activationurl = "<a href='https://www.example.com/#/activation?guid=" + AUserInfo.activationGUID + "'> Click here to activate your account </a>";
                body += "<br /><br />Welcome to Competition. We wish you a good luck. You know what to do to activate your account.";
                body += "<br />" + activationurl;
                body += "<br /><br />Thanks, CompetitionApp. </div></body></html>";
                mail.Body = body;
                mail.IsBodyHtml = true;
                mail.BodyEncoding = System.Text.Encoding.UTF8;
                mail.SubjectEncoding = System.Text.Encoding.Default;

                smtpClient.Send(mail);

                apiResponse.data.items = new List<bool> { true };
                apiResponse.success = true;
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.totalItemCount = apiResponse.data.items.LongCount(); //RecordCountBySql(sql); 
                apiResponse.data.page = 1;
                apiResponse.data.pageSize = apiResponse.data.items.LongCount();
            }
            catch (Exception ex1)
            {
                apiResponse.success = false;
                apiResponse.httpCode = StatusCodes.Status200OK;
                apiResponse.code = StatusCodes.Status400BadRequest;
                if (string.IsNullOrWhiteSpace(apiResponse.message)) apiResponse.message = ex1.Message;
                apiResponse.internalMessage = ex1.Message;
                if (hataUret) throw new Exception(apiResponse.message);
            }
            return Ok(apiResponse);
        }
    }
}
