using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CopilotUsageMeter.Readers.GitHub
{
    public sealed class GitHubCredentialStoreException : Exception
    {
        public GitHubCredentialStoreException() : base("Secure GitHub credential storage is unavailable.") { }
    }

    public sealed class GitHubCredential
    {
        public GitHubCredential(string login, string token) { Login = login; Token = token; }
        public string Login { get; }
        public string Token { get; }
        public override string ToString() => "GitHub credential (redacted)";
    }

    public interface IAccountCredentialStore
    {
        GitHubCredential? Load();
        GitHubCredential? ImportFromGitHubCli(string login);
        IReadOnlyList<string> ListGitHubCliLogins();
        void Save(GitHubCredential credential);
        void Clear();
    }

    // Never reads gh config, environment tokens or `gh auth token`: those can be plaintext fallbacks.
    public sealed class WindowsAccountCredentialStore : IAccountCredentialStore
    {
        private const string Target = "CopilotUsageMeter:github.com";
        public const string SignInInstructions = "Run gh auth login --web --hostname github.com, then connect using your GitHub login. Only Windows Credential Manager credentials are accepted; plaintext fallback is rejected.";

        public GitHubCredential? Load() => Read(Target, null);
        public GitHubCredential? ImportFromGitHubCli(string login)
        {
            if (!GitHubAccountReader.IsValidLogin(login)) throw new ArgumentException("Invalid GitHub login.", nameof(login));
            return Read("gh:github.com:" + login, login);
        }

        // Reads credential target names only; secrets are never opened during discovery.
        public IReadOnlyList<string> ListGitHubCliLogins()
        {
            EnsureWindows();
            const string prefix = "gh:github.com:";
            var logins = new List<string>();
            if (!CredEnumerate(prefix + "*", 0, out var count, out var array))
            {
                if (Marshal.GetLastWin32Error() == 1168) return logins;
                throw new GitHubCredentialStoreException();
            }
            try
            {
                for (var index = 0; index < count && logins.Count < 20; index++)
                {
                    var item = Marshal.ReadIntPtr(array, index * IntPtr.Size);
                    var native = Marshal.PtrToStructure<NativeCredential>(item);
                    var target = native.TargetName;
                    if (native.Type != 1 || target == null || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = target.Substring(prefix.Length);
                    if (GitHubAccountReader.IsValidLogin(name) && !logins.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
                        logins.Add(name);
                }
            }
            finally { CredFree(array); }
            return logins;
        }

        public void Save(GitHubCredential credential)
        {
            EnsureWindows();
            var bytes = Encoding.UTF8.GetBytes(credential.Token);
            if (bytes.Length == 0 || bytes.Length > 2560) throw new GitHubCredentialStoreException();
            var blob = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var native = new NativeCredential
                {
                    Type = 1, TargetName = Target, UserName = credential.Login,
                    CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob, Persist = 2
                };
                if (!CredWrite(ref native, 0)) throw new GitHubCredentialStoreException();
            }
            finally
            {
                for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(blob, index, 0);
                Marshal.FreeHGlobal(blob);
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public void Clear()
        {
            EnsureWindows();
            if (!CredDelete(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168)
                throw new GitHubCredentialStoreException();
        }

        private static GitHubCredential? Read(string target, string? login)
        {
            EnsureWindows();
            if (!CredRead(target, 1, 0, out var pointer))
            {
                if (Marshal.GetLastWin32Error() == 1168) return null;
                throw new GitHubCredentialStoreException();
            }
            try
            {
                var native = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (native.CredentialBlobSize == 0 || native.CredentialBlobSize > 2560) return null;
                var bytes = new byte[native.CredentialBlobSize];
                try
                {
                    Marshal.Copy(native.CredentialBlob, bytes, 0, bytes.Length);
                    string token;
                    try { token = new UTF8Encoding(false, true).GetString(bytes); }
                    catch (DecoderFallbackException) { return null; }
                    var name = login ?? native.UserName;
                    if (!GitHubAccountReader.IsValidLogin(name) || !GitHubAccountReader.IsValidToken(token)) return null;
                    return new GitHubCredential(name!, token);
                }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            finally { CredFree(pointer); }
        }

        private static void EnsureWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("Windows Credential Manager is required.");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags, Type;
            public string? TargetName, Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias, UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredEnumerate(string filter, uint flags, out int count, out IntPtr credentials);
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr credential);
    }
}