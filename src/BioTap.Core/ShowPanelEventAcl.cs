using System.Security.AccessControl;
using System.Security.Principal;

namespace BioTap.Core;

/// <summary>
/// ACL do evento "mostrar painel" (spec do launcher §3.3). O app elevado cria o evento; o launcher sem admin só pode
/// sinalizar e ler o dono. Um processo sem admin pode criar um objeto com o mesmo nome antes (squat), mas não consegue
/// pôr Administradores como dono, então o evento dele é recusado (e qualquer ACL diferente da nossa também). Só tipos do framework: arquivo linkado no launcher (AOT).
/// </summary>
public static class ShowPanelEventAcl
{
    public const string Name = @"Local\BioTap.ShowPanel";

    /// <summary>EVENT_MODIFY_STATE | READ_CONTROL.</summary>
    public const int UserRights = 0x2 | 0x20000;

    public static string Sddl(SecurityIdentifier user)
        => $"O:BAD:P(A;;0x1f0003;;;SY)(A;;0x1f0003;;;BA)(A;;0x{UserRights:x};;;{user.Value})";

    /// <summary>
    /// True só para exatamente a ACL que o app cria (dono, flags e ACEs, nessa ordem). Um subconjunto também seria
    /// seguro para quem sinaliza, mas aceitaria o evento só de admin de outro componente do sistema (alcançado, por
    /// exemplo, por um link simbólico do Object Manager plantado com o nosso nome) e o app consumiria os sinais dele.
    /// </summary>
    public static bool IsValid(string sddl, SecurityIdentifier user)
    {
        try
        {
            const AccessControlSections parts = AccessControlSections.Owner | AccessControlSections.Access;
            return new RawSecurityDescriptor(sddl).GetSddlForm(parts) == new RawSecurityDescriptor(Sddl(user)).GetSddlForm(parts);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
