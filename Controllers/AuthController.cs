using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SignalTracker.Controllers
{
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AuthController(ApplicationDbContext context)
        {
            _db = context;
        }

        [Authorize] // This attribute ensures only authenticated users can access this endpoint.
        [HttpGet("/api/auth/status")]
        public async Task<IActionResult> GetAuthStatus()
        {
            // Retrieve the user's email from the claims stored in the authentication cookie.
            var userEmail = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(userEmail))
            {
                // This should theoretically not happen if [Authorize] is working.
                return Unauthorized();
            }

            // Find the user in the database using the email from the cookie.
            var userDetails = await _db.tbl_user
                .Where(u => u.email == userEmail)
                .Select(u => new // Select only the data needed by the frontend.
                {
                    u.id,
                    u.name,
                    u.email,
                    u.m_user_type_id
                })
                .FirstOrDefaultAsync();

            if (userDetails == null)
            {
                // The user existed at login but is no longer in the database.
                return NotFound(new { message = "User not found." });
            }

            // Return the user data in the specific format the frontend AuthContext expects.
            return Ok(new { user = userDetails });
        }
    }
}