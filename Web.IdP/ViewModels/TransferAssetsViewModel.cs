using System.ComponentModel.DataAnnotations;

namespace Web.IdP.ViewModels;

public class TransferAssetsViewModel
{
    [Required]
    public Guid TargetPersonId { get; set; }
}
