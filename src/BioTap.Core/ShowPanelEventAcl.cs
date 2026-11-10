using System.Security.AccessControl;
using System.Security.Principal;

namespace BioTap.Core;

/// <summary>
/// ACL do evento "mostrar painel" (spec do launcher §3.3). O app elevado cria o evento; o launcher sem admin só pode
/// sinalizar e ler o dono. Um processo sem admin pode criar um objeto com o mesmo nome antes (squat), mas não consegue
/// pôr Administradores como dono, então o evento dele é recusado. Só tipos do framework: arquivo linkado no launcher (AOT).
/// </summary>
public static class ShowPanelEventAcl
{
    public const string Name = @"Local\BioTap.ShowPanel";

    /// <summary>EVENT_MODIFY_STATE | READ_CONTROL.</summary>
    public const int UserRights = 0x2 | 0x20000;

    static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static string Sddl(SecurityIdentifier user)
        => $"O:BAD:P(A;;0x1f0003;;;SY)(A;;0x1f0003;;;BA)(A;;0x{UserRights:x};;;{user.Value})";

    /// <summary>Dono SYSTEM/Administradores e nenhuma ACE de permissão além delas e do sinal do <paramref name="user"/>.</summary>
    public static bool IsValid(string sddl, SecurityIdentifier user)
    {
        try
        {
            var sd = new RawSecurityDescriptor(sddl);
            if (!IsTrusted(sd.Owner) || sd.DiscretionaryAcl is null) return false;
            foreach (var ace in sd.DiscretionaryAcl)
            {
                if (ace is not CommonAce a) return false; // object/callback ACE: tipo que um evento nosso nunca tem
                if (a.AceQualifier == AceQualifier.AccessDenied || IsTrusted(a.SecurityIdentifier)) continue; // negação não afrouxa nada
                if (a.AceQualifier != AceQualifier.AccessAllowed) return false;
                if (a.SecurityIdentifier != user || (a.AccessMask & ~UserRights) != 0) return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static bool IsTrusted(SecurityIdentifier? s) => s is not null && (s == SystemSid || s == Admins);
}
