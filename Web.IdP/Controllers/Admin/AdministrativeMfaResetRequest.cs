using System.ComponentModel.DataAnnotations;

namespace Web.IdP.Controllers.Admin;

public sealed record AdministrativeMfaResetRequest([Required, StringLength(500)] string Reason);
