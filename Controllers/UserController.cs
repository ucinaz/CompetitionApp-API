using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using CompetitionApp.Models;
using Dapper;
using Microsoft.Extensions.Hosting;

namespace CompetitionApp.Controllers
{
    [Route("api/User")]
    [ApiController]

    public class UserController : BaseController
    {
        public UserController(IConfiguration configuration, IHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
             : base(configuration, environment, httpContextAccessor)
        {
            //base
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

            var EmailUserInfo = conn.QueryFirstOrDefault<UserInfo>("select * from user_info where email = @email", new { email = AModel.email });
            if (EmailUserInfo != null) hataMesaji += "Böyle bir e-posta adresi zaten var!" + Environment.NewLine;

            var BUserInfo = conn.QueryFirstOrDefault<UserInfo>("select * from user_info where username = @username", new { username = AModel.username });
            if (BUserInfo != null) hataMesaji += "Böyle bir kullanıcı adı zaten var!" + Environment.NewLine;

            var ActivationUserInfo = conn.QueryFirstOrDefault<UserInfo>("select @username from user_info where userActivated = 1", new { username = AModel.username });
            if (ActivationUserInfo == null) hataMesaji += "Hesabınız Aktif değil, lütfen e-posta adresinize gelen linke tıklayarak hesabınızı aktif ediniz." + Environment.NewLine;

            if (hataMesaji != "") throw new Exception(hataMesaji);
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
                if (KayıtSayisi < 1) throw new Exception("Aktivasyon numarası bulunamadı!");

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

        [NonAction]
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