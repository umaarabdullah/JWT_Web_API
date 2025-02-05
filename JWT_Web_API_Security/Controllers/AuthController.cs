﻿using JWT_Web_API_Leads.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace JWT_Web_API_Leads.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        // stored user
        public static User user = new User();
        private readonly IConfiguration _configuration;
        private readonly IUserService _userService;

        public AuthController(IConfiguration configuration, IUserService userService)
        {
            _configuration = configuration;
            _userService = userService;
        }

        // Read claims in the controller in a secured way
        [HttpGet, Authorize]
        public ActionResult<string> GetUsername()
        {
            var userName = _userService.GetMyUsername();
            return Ok(userName);
            
            /*Another Approach achieving the same outcome*/
            /* Retrieves the username of the authenticated user and returns it as part of the response. */
            //var userName = User.Identity.Name;
            //var firstValueUsername = User.FindFirstValue(ClaimTypes.Name);
            //return Ok(new { userName, firstValueUsername }); 
            // return Ok(userName);
        }

        [HttpPost("register")]
        public async Task<ActionResult<User>> Register(UserDto request)
        {
            CreatePasswordHash(request.Password, out byte[] passwordHash, out byte[] passwordSalt);

            user.Username = request.Username;
            user.PasswordHash = passwordHash;
            user.PasswordSalt = passwordSalt;

            return Ok(user);
        }

        [HttpPost("login")]
        public async Task<ActionResult<string>> Login(UserDto request)
        {
            // check if user exists
            if(user.Username != request.Username)
            {
                return BadRequest("User not found");
            }

            if(!VerifyPasswordHash(request.Password, user.PasswordHash, user.PasswordSalt))
            {
                return BadRequest("Wrong Password.");
            }

            string jwtToken = GenerateJwtToken(user);

            var refreshToken = GenerateRefreshToken();
            SetRefreshToken(refreshToken);

            return Ok(jwtToken);
        }

        [HttpPost("refresh-token")]
        public async Task<ActionResult<string>> RefreshToken()
        {
            // get refresh token from cookies
            var refreshToken = Request.Cookies["refreshToken"];

            // validate refresh token
            if(!user.RefreshToken.Equals(refreshToken))
            {
                return Unauthorized("Invalid Refresh Token");
            }
            else if(user.TokenExpires < DateTime.Now)
            {
                return Unauthorized("Token Expired.");
            }

            // if valid refresh token then generate new JWT Token
            string token = GenerateJwtToken(user);
            var newRefreshToken = GenerateRefreshToken();
            SetRefreshToken(newRefreshToken);

            return Ok(token);   // returns new jwt token in the response body
        }

        private RefreshToken GenerateRefreshToken()
        {
            // keep refresh token expiry time configurable in appsettings.json
            var refreshTokenExpTimeMinStr = _configuration.GetSection("AppSettings:RefreshTokenExpTimeMin").Value;
            int refreshTokenExpTimeMin = int.Parse(refreshTokenExpTimeMinStr);
            Console.WriteLine(DateTime.Now);
            Console.WriteLine(refreshTokenExpTimeMin);

            var refreshToken = new RefreshToken
            {
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                Expires = DateTime.Now.AddMinutes(refreshTokenExpTimeMin),
                Created = DateTime.Now
            };

            return refreshToken;
        }

        private void SetRefreshToken(RefreshToken newRefreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = newRefreshToken.Expires
            };
            Response.Cookies.Append("refreshToken", newRefreshToken.Token, cookieOptions);

            user.RefreshToken = newRefreshToken.Token;
            user.TokenCreated = newRefreshToken.Created;
            user.TokenExpires = newRefreshToken.Expires;

            Console.WriteLine(user.TokenExpires);
        }

        private string GenerateJwtToken(User user)
        {
            // claims are properties which can help validate the token and can be read with a corresponding client application
            List<Claim> claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username)
            };


            /* Creating a custom json web token authentication */

            // creating a key from secret key defined in appsettings
            var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(
                _configuration.GetSection("AppSettings:Token").Value));     // secret key defined in appsettings.json

            var sigining_credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

            // JWT token expiration time made configurable by defining value in appsettings.json file
            var JwtExpTimeMinStr = _configuration.GetSection("AppSettings:JwtTokenExpTimeMin").Value;
            int JwtExpTimeMin = int.Parse(JwtExpTimeMinStr);

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.Now.AddMinutes(JwtExpTimeMin),    // configuring jwt expiration time 
                signingCredentials: sigining_credentials
            );

            // The final json web token
            var jwt = new JwtSecurityTokenHandler().WriteToken(token);

            return jwt;
        }

        // out keyword in C# is a parameter modifier that allows you to pass an argument to a method by reference instead of by value
        // Password salting is a security process that involves adding random strings and integers to passwords before hashing them.
        private void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
        {
            using(var hmac = new HMACSHA512())
            {
                passwordSalt = hmac.Key;
                passwordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
            }
        }

        private bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
        {
            using (var hmac = new HMACSHA512(passwordSalt))
            {
                var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return computedHash.SequenceEqual(passwordHash);    // if ture user has entered the correct username  and password
            }
        }
    }
}
