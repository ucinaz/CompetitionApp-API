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
    [Route("api/Options")]
    [ApiController]
    public class OptionsController : BaseController
    {
        public OptionsController(IConfiguration configuration, IHostEnvironment environment, IHttpContextAccessor httpContextAccessor)
             : base(configuration, environment, httpContextAccessor)
        {
            //base
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

        [NonAction]
        private void OptionsChosenGecerlilikKontrolu(OptionsChosen AModel, long? sonKayitRef)
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

        [NonAction]
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
                OptionsChosenGecerlilikKontrolu(AModel, sonKayitRef);
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

        [NonAction]
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

    }
}