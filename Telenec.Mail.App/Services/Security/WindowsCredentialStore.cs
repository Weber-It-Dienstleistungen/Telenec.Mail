using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Telenec.Mail.App.Services.Security;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public Task<bool> ExistsAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetName = GetTargetName(accountId);

        if (CredRead(
                targetName,
                CredentialTypeGeneric,
                0,
                out var credentialPointer))
        {
            CredFree(credentialPointer);

            return Task.FromResult(true);
        }

        var error = Marshal.GetLastWin32Error();

        if (error == ErrorNotFound)
        {
            return Task.FromResult(false);
        }

        throw new Win32Exception(error);
    }

    public Task<StoredCredential?> ReadAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetName = GetTargetName(accountId);

        if (!CredRead(
                targetName,
                CredentialTypeGeneric,
                0,
                out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();

            if (error == ErrorNotFound)
            {
                return Task.FromResult<StoredCredential?>(
                    null);
            }

            throw new Win32Exception(error);
        }

        try
        {
            var credential =
                Marshal.PtrToStructure<NativeCredential>(
                    credentialPointer);

            var password =
                credential.CredentialBlob == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringUni(
                        credential.CredentialBlob,
                        checked(
                            (int)credential.CredentialBlobSize / 2))
                      ?? string.Empty;

            var result =
                new StoredCredential
                {
                    UserName =
                        credential.UserName
                        ?? string.Empty,

                    Password = password
                };

            return Task.FromResult<StoredCredential?>(
                result);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public Task SaveAsync(
        Guid accountId,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException(
                "Der Benutzername darf nicht leer sein.",
                nameof(userName));
        }

        ArgumentNullException.ThrowIfNull(password);

        var passwordPointer =
            Marshal.StringToCoTaskMemUni(password);

        try
        {
            var credential =
                new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName =
                        GetTargetName(accountId),

                    UserName = userName,

                    CredentialBlob =
                        passwordPointer,

                    CredentialBlobSize =
                        checked(
                            (uint)(password.Length * sizeof(char))),

                    Persist =
                        CredentialPersistLocalMachine
                };

            if (!CredWrite(
                    ref credential,
                    0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(
                passwordPointer);
        }
    }

    public Task DeleteAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetName =
            GetTargetName(accountId);

        if (CredDelete(
                targetName,
                CredentialTypeGeneric,
                0))
        {
            return Task.CompletedTask;
        }

        var error =
            Marshal.GetLastWin32Error();

        if (error == ErrorNotFound)
        {
            return Task.CompletedTask;
        }

        throw new Win32Exception(error);
    }

    private static string GetTargetName(
        Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }

        return $"Telenec.Mail:{accountId:D}";
    }

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Comment;

        public FILETIME LastWritten;

        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;

        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? TargetAlias;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? UserName;
    }

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredWriteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredReadW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPtr);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredDeleteW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(
        string target,
        uint type,
        uint flags);

    [DllImport(
        "advapi32.dll",
        EntryPoint = "CredFree")]
    private static extern void CredFree(
        IntPtr buffer);
}