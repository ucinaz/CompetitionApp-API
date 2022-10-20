using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using CompetitionApp.Models;
using Dapper;
using Microsoft.Extensions.Hosting;
using System.Net.Mail;
using SmtpClient = System.Net.Mail.SmtpClient;

namespace CompetitionApp.Controllers
{
    [Route("api/")]
    [ApiController]

    public class ApplicationController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _enviroment;
        private readonly SqlConnection conn;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ApplicationController(IConfiguration configuration, IHostEnvironment environment, IHttpContextAccessor httpContextAccessor/*, IMailService mailService*/)
        {
            this._configuration = configuration;
            this._enviroment = environment;
            conn = new SqlConnection(GetCnnStr());
            _httpContextAccessor = httpContextAccessor;
        }

        private string GetClaimValueByName(string UserId, string VarsayilanDeger, bool HataUret)
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


        [HttpGet]
        public int GetActiveUserId()
        {
            var CValue = GetClaimValueByName("UserId", "-1", true);
            return Int32.Parse(CValue);
        }

        [HttpGet("active")]
        public UserInfo GetActiveUser()
        {
            string SqlStr = "";
            SqlStr += "SELECT * from user_info where user_id = @user_id";
            return conn.QueryFirstOrDefault<UserInfo>(SqlStr, new { user_id = GetActiveUserId()});
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

        private string GetCnnStr()
        {
            string CnnName = "CompetitionAppCnn";
            return _configuration["ConnectionStrings:" + CnnName];
        }

        [HttpGet("user-list")]
        public IActionResult UserInfoListe(int kayitRef, string arama, int page, int pageSize)
        {
            serviceResponse<UserInfo> apiResponse = new();
            try
            {
                if (page == 0) page = 1;
                if (pageSize == 0) pageSize = 50;
                if (string.IsNullOrWhiteSpace(arama)) arama = "";
                if (arama != "") arama = "%" + arama + "%";

                var prm = new
                {
                    arama1 = arama,
                    arama2 = arama,
                    user_id = kayitRef,
                    offset = (page - 1) * pageSize,
                    pageSize = pageSize
                };

                string sql = "";
                sql += "SELECT ";
                sql += "    tb.* ";
                sql += "FROM  ";
                sql += "    user_info tb  ";
                sql += "WHERE ";
                sql += "  (1 = 1)  ";
                if (arama != "")
                {
                    sql += "  and (tb.adi like @arama1 or tb.kod like @arama2) ";
                }

                if (kayitRef > 0)
                {
                    sql += " AND (tb.user_id = @user_id) ";
                }

                // where'den sonra order by ve paging'den önce toplam kayıt sayısı tespit ediliyor. 
                apiResponse.data.totalItemCount = conn.Query<long>("select count(*) from (" + sql + ") as tbl1", prm).SingleOrDefault();

                sql += "ORDER BY  ";
                sql += "    tb.user_id desc ";
                sql += "OFFSET @offset ROWS  ";
                sql += "FETCH NEXT @pageSize ROWS ONLY ";

                apiResponse.success = true;
                apiResponse.data.items = conn.Query<UserInfo>(sql, prm);
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.page = page;
                apiResponse.data.pageSize = pageSize;
            }
            catch (Exception ex1)
            {
                apiResponse.success = false;
                apiResponse.httpCode = StatusCodes.Status200OK;
                apiResponse.code = StatusCodes.Status400BadRequest;
                if (string.IsNullOrEmpty(apiResponse.message)) apiResponse.message = ex1.Message;
                apiResponse.internalMessage = ex1.Message;
                if (ex1.InnerException != null) apiResponse.internalMessage = ex1.InnerException.Message;
            }
            return Ok(apiResponse);
        }

        private void UserInfoGecerlilikKontrolu(UserInfo AModel, long? sonKayitRef)
        {
            string hataMesaji = "";

            if (string.IsNullOrEmpty(AModel.username)) hataMesaji += "Lütfen kullanıcı adınızı giriniz." + Environment.NewLine;
            if (string.IsNullOrEmpty(AModel.email)) hataMesaji += "Lütfen E-Posta Adresinizi giriniz." + Environment.NewLine;
            if (string.IsNullOrEmpty(AModel.password)) hataMesaji += "Lütfen şifrenizi giriniz." + Environment.NewLine;
            if (string.IsNullOrEmpty(AModel.firstName)) hataMesaji += "Lütfen adınızı giriniz." + Environment.NewLine;
            if (string.IsNullOrEmpty(AModel.lastName)) hataMesaji += "Lütfen soyadınızı giriniz." + Environment.NewLine;

            var EmailUserInfo = conn.QueryFirstOrDefault<UserInfo>("select * from user_info where email = @email", new { email = AModel.email});
            if (EmailUserInfo != null) hataMesaji += "Böyle bir e-posta adresi zaten var!" + Environment.NewLine;

            var BUserInfo = conn.QueryFirstOrDefault<UserInfo>("select * from user_info where username = @username", new { username = AModel.username });
            if (BUserInfo != null) hataMesaji += "Böyle bir kullanıcı adı zaten var!" + Environment.NewLine;

            var ActivationUserInfo = conn.QueryFirstOrDefault<UserInfo>("select @username from user_info where userActivated = 1", new { username = AModel.username });
            if (ActivationUserInfo == null) hataMesaji += "Hesabınız Aktif değil, lütfen e-posta adresinize gelen linke tıklayarak hesabınızı aktif ediniz." + Environment.NewLine;

            if (hataMesaji != "") throw new Exception(hataMesaji);
        }

        [HttpGet("send-activation-email")]
        public ActionResult AktivasyonEPostaGonder(int aUserId, bool hataUret = false) 
        {
            serviceResponse<bool> apiResponse = new ();
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
                string body = "<!DOCTYPE html><html><body>" +"<div> Hey " + AUserInfo.username + ",";
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

        [HttpPost("user-kayit")]
        public ActionResult UserInfoEkle([FromBody] UserInfo AModel)
        {
            serviceResponse<UserInfo> apiResponse = new serviceResponse<UserInfo>();
            try
            {
                int sonKayitRef = 0;
                if (AModel.user_id > 0) { sonKayitRef = AModel.user_id; }
                else
                {
                    sonKayitRef = conn.Query<int>("select COALESCE(max(user_id), 0) from user_info").Single();
                    sonKayitRef += 1;
                }
                UserInfoGecerlilikKontrolu(AModel, sonKayitRef);
                string sql = @" 
                    INSERT INTO 
                    user_info ( 
                        user_id, 
                        username, 
                        password, 
                        firstName, 
                        lastName, 
                        email, 
                        registerDate) 
                    VALUES ( 
                        @user_id, 
                        @username, 
                        @password, 
                        @firstName, 
                        @lastName, 
                        @email, 
                        @registerDate); 
                ";
                if (AModel.user_id > 0)
                {
                    sql = @" 
                    UPDATE 
                        user_info 
                    SET 
                        username = @username, 
                        password = @password, 
                        firstName = @firstName, 
                        lastName = @lastName, 
                        email = @email, 
                        registerDate = @registerDate 
                    WHERE 
                        user_id = @user_id 
                    ";
                }

                var prm = new
                {
                    user_id = sonKayitRef, //AModel.user_id,
                    username = AModel.username,
                    password = AModel.password,
                    firstName = AModel.firstName,
                    lastName = AModel.lastName,
                    email = AModel.email,
                    registerDate = AModel.registerDate
                };

                if (AModel.user_id > 0)
                {
                    var affectRows = conn.Execute(sql, prm);

                }
                else
                {
                    var affectRows = conn.Execute(sql, prm);
                    AModel.user_id = sonKayitRef;
                    
                }
                apiResponse.data.items = new List<UserInfo> { AModel };
                apiResponse.success = true;
                var emailsent = AktivasyonEPostaGonder(AModel.user_id, true);
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
                if (string.IsNullOrWhiteSpace(apiResponse.message)) apiResponse.message = ex1.Message + " Lütfen tekrar kayıt olmayı deneyin.";
                apiResponse.internalMessage = ex1.Message;

            }
            return Ok(apiResponse);
        }


        [HttpPost("user-activate")]
        public ActionResult UserActivate([FromBody] UserInfo AModel)
        {
            string sql = "";

            serviceResponse<UserInfo> apiResponse = new serviceResponse<UserInfo>();
            try
            {
                if (AModel.activationGUID == null) throw new Exception("Aktivasyon numarası boş gönderilemez!");
                sql = @" 
                    UPDATE 
                        user_info 
                    SET 
                        userActivated = 1 
                    WHERE 
                        activationGUID = @activationGUID 
                    ";
                var KayıtSayisi = conn.Execute(sql, new { activationGUID = AModel.activationGUID });
                if  (KayıtSayisi < 1) throw new Exception("Aktivasyon numarası bulunamadı!");

                sql = @" 
                    SELECT 
                        * 
                    FROM
                        user_info
                    WHERE 
                        activationGUID = @activationGUID 
                    ";
                var BModel = conn.QueryFirstOrDefault<UserInfo>(sql, new { activationGUID = AModel.activationGUID });

                var updateUserActivation = "update user_info set userActivated = 1 where user_id = @user_id";
                var qUpdateUserActivation = conn.Query<OptionsChosen>(updateUserActivation, new { user_id = BModel.user_id }).FirstOrDefault();
                AModel.password = "";
                apiResponse.data.items = new List<UserInfo> { AModel };
                apiResponse.success = true;
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.totalItemCount = apiResponse.data.items.LongCount();
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
            }
            return Ok(apiResponse);
        }

                
        [HttpGet("option-chosen-liste")]
        public IActionResult optionChosenListe(int kayitRef, int userId, string arama, int page, int pageSize)
        {
            serviceResponse<OptionsChosen> apiResponse = new();
            try
            {
                if (page == 0) page = 1;
                if (pageSize == 0) pageSize = 50;
                if (string.IsNullOrWhiteSpace(arama)) arama = "";
                if (arama != "") arama = "%" + arama + "%";

                // Sorgu parametreleri 
                var prm = new
                {
                    arama1 = arama,
                    arama2 = arama,
                    id = kayitRef,
                    userId = userId,
                    offset = (page - 1) * pageSize,
                    pageSize = pageSize
                };

                string sql = "";
                sql += "SELECT ";
                sql += "    tb.* ";
                sql += "FROM  ";
                sql += "    vw_options_chosen tb  ";
                sql += "WHERE ";
                sql += "  (1 = 1)  ";
                if (arama != "")
                {
                    sql += "  and (tb.adi like @arama1 or tb.kod like @arama2) ";
                }

                if (kayitRef > 0)
                {
                    sql += " AND (tb.id = @id) ";
                }

                if (userId > 0)
                {
                    sql += " AND (tb.user_id = @userId) ";
                }

                // where'den sonra order by ve paging'den önce toplam kayıt sayısı tespit ediliyor. 
                apiResponse.data.totalItemCount = conn.Query<long>("select count(*) from (" + sql + ") as tbl1", prm).SingleOrDefault();

                sql += "ORDER BY  ";
                sql += "    tb.id desc ";
                sql += "OFFSET @offset ROWS  ";
                sql += "FETCH NEXT @pageSize ROWS ONLY ";

                apiResponse.success = true;
                apiResponse.data.items = conn.Query<OptionsChosen>(sql, prm);
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.page = page;
                apiResponse.data.pageSize = pageSize;
            }
            catch (Exception ex1)
            {
                apiResponse.success = false;
                apiResponse.httpCode = StatusCodes.Status200OK;
                apiResponse.code = StatusCodes.Status400BadRequest;
                if (string.IsNullOrEmpty(apiResponse.message)) apiResponse.message = ex1.Message;
                apiResponse.internalMessage = ex1.Message;
                if (ex1.InnerException != null) apiResponse.internalMessage = ex1.InnerException.Message;
            }
            return Ok(apiResponse);
        }

        private void optionsChosenGecerlilikKontrolu(OptionsChosen AModel, long? sonKayitRef)
        {
            string hataMesaji = "";
            var ActiveUserId = GetActiveUserId();
            var sql = "";
            AModel.user_id = ActiveUserId;

            // Yeni kayıtsa 
            if (AModel.id <= 0)
            {
                AModel.date_added = DateTime.Now;
                AModel.date_last_updated = DateTime.Now;

                sql = "select id, date_added from options_chosen where user_id = @user_id";
                var QoptionsChosen = conn.Query<OptionsChosen>(sql, new { user_id = AModel.user_id }).FirstOrDefault();
                if (QoptionsChosen != null) hataMesaji += "Bu kullanıcının " + QoptionsChosen.date_added.ToString("dd.MM.yyyy") + " tarihinde girilmiş bir kaydı zaten var." + Environment.NewLine;
            }

            sql = "select user_id from user_info where user_id = @user_id";
            var QUserInfo = conn.Query<OptionsChosen>(sql, new { user_id = AModel.user_id }).FirstOrDefault();
            if (QUserInfo == null) hataMesaji += AModel.user_id.ToString() + " id numaralı kullanıcı bulunamadı! Tekrar giriş yapınız!" + Environment.NewLine;

            int[] options = { AModel.option_1_id, AModel.option_2_id, AModel.option_3_id, AModel.option_4_id, AModel.option_5_id, AModel.option_6_id, AModel.option_7_id, AModel.option_8_id, AModel.option_9_id, AModel.option_10_id };
            if (ContainsDuplicate(options)) hataMesaji += "Sectiginiz tum atlar birbirinden farkli olmalidir." + Environment.NewLine;

            if (AModel.id > 0)
            {
             
                AModel.date_last_updated = DateTime.Now;
            }

            if (hataMesaji != "") throw new Exception(hataMesaji);
        }

        public static bool ContainsDuplicate(int[] nums)
        {
            var temp = new HashSet<Int32>();

            for (int i = 0; i < nums.Length; i++)
            {
                if (temp.Contains(nums[i]))
                {
                    return true;
                }
                temp.Add(nums[i]);
            }
            return false;
        }

        [HttpPost("options-chosen-kayit")]
        public ActionResult optionsChosenKayit([FromBody] OptionsChosen AModel)
        {
            serviceResponse<OptionsChosen> apiResponse = new serviceResponse<OptionsChosen>();
            try
            {
                var updateActivation = "";
                AModel.user_id = GetActiveUserId();
                int sonKayitRef = 0;
                if (AModel.id > 0) { sonKayitRef = AModel.id; }
                else
                {
                    sonKayitRef = conn.Query<int>("select COALESCE(max(id), 0) from options_chosen").Single();
                    sonKayitRef += 1;
                }
                optionsChosenGecerlilikKontrolu(AModel, sonKayitRef);
                string sql = @" 
                    INSERT INTO 
                    options_chosen ( 
                        id, 
                        user_id, 
                        option_1_id, 
                        option_2_id, 
                        option_3_id, 
                        option_4_id, 
                        option_5_id, 
                        option_6_id, 
                        option_7_id, 
                        option_8_id, 
                        option_9_id, 
                        option_10_id
                        ) 
                    VALUES ( 
                        @id, 
                        @user_id, 
                        @option_1_id, 
                        @option_2_id, 
                        @option_3_id, 
                        @option_4_id, 
                        @option_5_id, 
                        @option_6_id, 
                        @option_7_id, 
                        @option_8_id, 
                        @option_9_id, 
                        @option_10_id
                        ); 
                ";
                if (AModel.id > 0)
                {
                    sql = @" 
                    UPDATE 
                        options_chosen 
                    SET 
                        user_id = @user_id, 
                        option_1_id = @option_1_id, 
                        option_2_id = @option_2_id, 
                        option_3_id = @option_3_id, 
                        option_4_id = @option_4_id, 
                        option_5_id = @option_5_id, 
                        option_6_id = @option_6_id, 
                        option_7_id = @option_7_id, 
                        option_8_id = @option_8_id, 
                        option_9_id = @option_9_id, 
                        option_10_id = @option_10_id
                        WHERE 
                        id = @id 
                    ";
                }

                var prm = new
                {
                    id = sonKayitRef,
                    user_id = AModel.user_id,
                    option_1_id = AModel.option_1_id,
                    option_2_id = AModel.option_2_id,
                    option_3_id = AModel.option_3_id,
                    option_4_id = AModel.option_4_id,
                    option_5_id = AModel.option_5_id,
                    option_6_id = AModel.option_6_id,
                    option_7_id = AModel.option_7_id,
                    option_8_id = AModel.option_8_id,
                    option_9_id = AModel.option_9_id,
                    option_10_id = AModel.option_10_id
                };

                if (AModel.id > 0)
                {
                    var affectRows = conn.Execute(sql, prm);
                    updateActivation = "update user_info set contestRegistered = 1 where user_id = @user_id";
                    var qUpdateActivation = conn.Query<OptionsChosen>(updateActivation, new { user_id = AModel.user_id }).FirstOrDefault();
                }
                else
                {
                    var affectRows = conn.Execute(sql, prm);
                    updateActivation = "update user_info set contestRegistered = 1 where user_id = @user_id";
                    var qUpdateActivation = conn.Query<OptionsChosen>(updateActivation, new { user_id = AModel.user_id }).FirstOrDefault();
                    AModel.id = sonKayitRef;
                }
                apiResponse.data.items = new List<OptionsChosen> { AModel };
                apiResponse.success = true;
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.totalItemCount = apiResponse.data.items.LongCount();
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
            }
            return Ok(apiResponse);
        }

        [HttpGet("option-chosen-sil")]
        public IActionResult RegisterModelSil(long kayitRef)
        {
            serviceResponse<OptionsChosen> apiResponse = new();
            try
            {
                string SqlStr = "";
                SqlStr += "DELETE ";
                SqlStr += "FROM  ";
                SqlStr += "  options_chosen  ";
                SqlStr += "WHERE  ";
                SqlStr += "  id = @id  ";
                var prm = new
                {
                    id = kayitRef
                };
                conn.Execute(SqlStr, prm);
                apiResponse.success = true;
                //apiResponse.data.items = { }; 
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.page = 1;
                apiResponse.data.pageSize = 1;
            }
            catch (Exception ex1)
            {
                apiResponse.success = false;
                apiResponse.httpCode = StatusCodes.Status200OK;
                apiResponse.code = StatusCodes.Status400BadRequest;
                if (string.IsNullOrEmpty(apiResponse.message)) apiResponse.message = ex1.Message;
                apiResponse.internalMessage = ex1.Message;
                if (ex1.InnerException != null) apiResponse.internalMessage = ex1.InnerException.Message;
            }
            return Ok(apiResponse);
        }

        [HttpGet("user-list-liste")]
        public IActionResult UserListListe(int kayitRef, string arama, int page, int pageSize)
        {
            serviceResponse<UserList> apiResponse = new();
            try
            {
                if (page == 0) page = 1;
                if (pageSize == 0) pageSize = 50;
                if (string.IsNullOrWhiteSpace(arama)) arama = "";
                if (arama != "") arama = "%" + arama + "%";

                // Sorgu parametreleri 
                var prm = new
                {
                    arama1 = arama,
                    arama2 = arama,
                    id = kayitRef,
                    offset = (page - 1) * pageSize,
                    pageSize = pageSize
                };

                string sql = "";
                sql += "SELECT ";
                sql += "    tb.* ";
                sql += "FROM  ";
                sql += "    vw_user_list tb  ";
                sql += "WHERE ";
                sql += "  (1 = 1)  ";
                if (arama != "")
                {
                    sql += "  and (tb.adi like @arama1 or tb.kod like @arama2) ";
                }

                if (kayitRef > 0)
                {
                    sql += " AND (tb.id = @id) ";
                }

                // where'den sonra order by ve paging'den önce toplam kayıt sayısı tespit ediliyor. 
                apiResponse.data.totalItemCount = conn.Query<long>("select count(*) from (" + sql + ") as tbl1", prm).SingleOrDefault();

                sql += "ORDER BY  ";
                sql += "    tb.id desc ";
                sql += "OFFSET @offset ROWS  ";
                sql += "FETCH NEXT @pageSize ROWS ONLY ";

                apiResponse.success = true;
                apiResponse.data.items = conn.Query<UserList>(sql, prm);
                apiResponse.data.itemCount = apiResponse.data.items.LongCount();
                apiResponse.data.page = page;
                apiResponse.data.pageSize = pageSize;
            }
            catch (Exception ex1)
            {
                apiResponse.success = false;
                apiResponse.httpCode = StatusCodes.Status200OK;
                apiResponse.code = StatusCodes.Status400BadRequest;
                if (string.IsNullOrEmpty(apiResponse.message)) apiResponse.message = ex1.Message;
                apiResponse.internalMessage = ex1.Message;
                if (ex1.InnerException != null) apiResponse.internalMessage = ex1.InnerException.Message;
            }
            return Ok(apiResponse);
        }

        
        [HttpGet("options-liste")] 
        public IActionResult optionsListe(int kayitRef, string arama, int page, int pageSize) 
        { 
            serviceResponse<Options> apiResponse = new (); 
            try 
            { 
                if (page == 0) page = 1; 
                if (pageSize == 0) pageSize = 50; 
                if (string.IsNullOrWhiteSpace(arama)) arama = ""; 
                if (arama != "") arama = "%" + arama + "%"; 

                var prm = new 
                { 
                    arama1 = arama, 
                    arama2 = arama, 
                    id = kayitRef, 
                    offset = (page - 1) * pageSize, 
                    pageSize = pageSize 
                }; 
 
                string sql = ""; 
                sql += "SELECT "; 
                sql += "    tb.* "; 
                sql += "FROM  "; 
                sql += "    options tb  "; 
                sql += "WHERE "; 
                sql += "  (1 = 1)  "; 
                if (arama != "") 
                { 
                    sql += "  and (tb.option_name like @arama1 or tb.id like @arama2) "; 
                } 
 
                if (kayitRef > 0) 
                { 
                    sql += " AND (tb.id = @id) "; 
                } 
 
                // where'den sonra order by ve paging'den önce toplam kayıt sayısı tespit ediliyor. 
                apiResponse.data.totalItemCount = conn.Query<long>("select count(*) from (" + sql + ") as tbl1", prm).SingleOrDefault(); 
 
                sql += "ORDER BY  "; 
                sql += "    tb.id desc "; 
                sql += "OFFSET @offset ROWS  "; 
                sql += "FETCH NEXT @pageSize ROWS ONLY "; 
 
                apiResponse.success = true; 
                apiResponse.data.items = conn.Query<Options>(sql, prm); 
                apiResponse.data.itemCount = apiResponse.data.items.LongCount(); 
                apiResponse.data.page = page; 
                apiResponse.data.pageSize = pageSize; 
            } 
            catch (Exception ex1) 
            { 
                apiResponse.success = false; 
                apiResponse.httpCode = StatusCodes.Status200OK; 
                apiResponse.code = StatusCodes.Status400BadRequest; 
                if (string.IsNullOrEmpty(apiResponse.message)) apiResponse.message = ex1.Message; 
                apiResponse.internalMessage = ex1.Message; 
                if (ex1.InnerException != null) apiResponse.internalMessage = ex1.InnerException.Message; 
            } 
            return Ok(apiResponse); 
        } 
 
        private void optionsGecerlilikKontrolu(Options AModel, long? sonKayitRef) 
        { 
            string hataMesaji = ""; 
 
            // Yeni kayıtsa 
            if (AModel.id <= 0) 
            { 

            } 
 
            if (AModel.id > 0) 
            { 

            } 
 
            if (hataMesaji != "") throw new Exception(hataMesaji); 
        } 
 
        [HttpPost("options-kayit")] 
        public ActionResult optionsEkle([FromBody] Options AModel) 
        { 
            serviceResponse<Options> apiResponse = new serviceResponse<Options>(); 
            try 
            { 
                int sonKayitRef = 0; 
                if (AModel.id > 0) { sonKayitRef = AModel.id; } 
                else 
                { 
                    sonKayitRef = conn.Query<int>("select COALESCE(max(id), 0) from options").Single(); 
                    sonKayitRef += 1; 
                } 
                optionsGecerlilikKontrolu(AModel, sonKayitRef); 
                string sql = @" 
                    INSERT INTO 
                    options ( 
                        id, 
                        option_name, 
                        option_family, 
                        option_point) 
                    VALUES ( 
                        @id, 
                        @option_name, 
                        @option_family, 
                        @option_point); 
                "; 
                if (AModel.id > 0) 
                { 
                    sql = @" 
                    UPDATE 
                        options 
                    SET 
                        option_name = @option_name, 
                        option_family = @option_family, 
                        option_point = @option_point 
                    WHERE 
                        id = @id 
                    "; 
                } 
 
                var prm = new 
                { 
                    id = AModel.id,
                    option_name = AModel.option_name,
                    option_point = AModel.option_point
                }; 
 
                if (AModel.id > 0) 
                { 
                    var affectRows = conn.Execute(sql, prm); 
                } 
                else 
                { 
                    var affectRows = conn.Execute(sql, prm); 
                    AModel.id = sonKayitRef; 
                } 
                apiResponse.data.items = new List<Options> { AModel }; 
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
            } 
            return Ok(apiResponse); 
        } 
 

        [HttpPost("authenticate")]
        public ActionResult Authentication()
        {
            serviceResponse<UserInfo> service_Result = new();

            var CurrentUser = GetActiveUser();
                       
            try
            {
                if (CurrentUser.isAdmin == 1)
                {
                    service_Result.data.itemCount = service_Result.data.items.LongCount();
                    service_Result.data.totalItemCount = service_Result.data.items.LongCount();
                    service_Result.data.page = 1;
                    service_Result.data.pageSize = service_Result.data.items.LongCount();
                    service_Result.success = true;
                    service_Result.internalMessage = "Success";
                    return Ok(service_Result);
                }
                else
                {
                    throw new System.Exception("Burayi goruntulemeye yetkiniz yok.");
                }
            }
            catch (Exception ex2)
            {
                service_Result.success = false;
                service_Result.httpCode = StatusCodes.Status200OK;
                service_Result.code = StatusCodes.Status400BadRequest;
                if (string.IsNullOrEmpty(service_Result.message)) service_Result.message = ex2.Message;
                service_Result.internalMessage = ex2.Message;
                return Ok(service_Result);
            }
        }

    }
}