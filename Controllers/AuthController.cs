using CompetitionApp.Models;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace CompetitionApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public AuthController(IConfiguration configuration)
        {
            this._configuration = configuration;
        }

        private string CreateToken(UserInfo model)
        {
            //security key
            string securityKey = _configuration["Jwt:SigningKey"];

            //symmetric security key
            var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityKey));

            //signing credentials
            var signingCredentials = new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256Signature);

            //add claims
            var claims = new List<Claim>();
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
            claims.Add(new Claim("UserId", model.user_id.ToString()));

            // create token
            var token = new JwtSecurityToken(
                issuer: "smesk.in",
                audience: "readers",
                expires: DateTime.Now.AddHours(4), // 4 saat süre
                signingCredentials: signingCredentials,
                claims: claims
            );

            //return token
            var newToken = new JwtSecurityTokenHandler().WriteToken(token);
            return newToken;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public ActionResult Login([FromBody] UserInfo model)
        {
            serviceResponse<UserInfo> service_Result = new ();
            try
            {
                if (model.username == null) model.username = "";
                if (model.email == null) model.email = "";
                if (model.password == null) model.password = "";

                model.password = model.password.Trim();
                if (model.username == "" && model.email == "") throw new System.Exception("Lütfen kullanıcı adınızı giriniz!");
                if (model.password == "") throw new System.Exception("Lütfen şifrenizi giriniz!");
                

                model = checkUserInfo(model);
                if (model != null)
                {
                    model.jwt_key = CreateToken(model);
                    model.password = "";
                    service_Result.data.items = new List<UserInfo> { model };
                    service_Result.data.itemCount = service_Result.data.items.LongCount();
                    service_Result.data.totalItemCount = service_Result.data.items.LongCount();
                    service_Result.data.page = 1;
                    service_Result.data.pageSize = service_Result.data.items.LongCount();
                    service_Result.success = true;
                    service_Result.message = "Login başarılı (" + model.user_id.ToString() + ")!";
                    service_Result.internalMessage = "Success";
                    return Ok(service_Result);
                }
                else
                {
                    throw new System.Exception("Yanlış kullanıcı adı veya şifre!");
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

       
        private UserInfo checkUserInfo(UserInfo AUserInfo)
        {
            //if (String.IsNullOrEmpty(AUserInfo.aktif_cnn)) AUserInfo.aktif_cnn = "DefaultConnection";
            var cnnStr = _configuration["ConnectionStrings:CompetitionAppCnn"];
            SqlConnection conn1 = new SqlConnection(cnnStr);
            string SQL = @"
                            SELECT 
                                ui.*
                            FROM
                                dbo.user_info ui
                            WHERE 
                                (ui.username = @username or ui.email = @email) 
                                and ui.password = @password";
            var prm = new
            {
                username = AUserInfo.username,
                email = AUserInfo.username,
                password = AUserInfo.password
            };

            var user = conn1.Query<UserInfo>(SQL, prm).FirstOrDefault();
            if (user != null)
            {
                return user;
            }
            else throw new System.Exception("Yanlış kullanıcı adı veya şifre!");
        }

       
    }
}
