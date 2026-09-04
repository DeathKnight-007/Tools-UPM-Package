using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DeathKnight.Net.Editor
{
    internal static class CertificateUtility
    {
        private const string PemBegin = "-----BEGIN CERTIFICATE-----";
        private const string PemEnd = "-----END CERTIFICATE-----";

        internal static X509Certificate2 Load(string path, string password)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("请选择证书文件。", nameof(path));

            byte[] data = File.ReadAllBytes(path);
            string text = Encoding.ASCII.GetString(data);
            if (text.IndexOf(PemBegin, StringComparison.Ordinal) >= 0)
            {
                byte[] der = DecodePem(text);
                try { return new X509Certificate2(der); }
                finally { Array.Clear(der, 0, der.Length); }
            }

            return Import(data, password ?? string.Empty);
        }

        internal static X509Certificate2 CreateRoot(
            string commonName, string organization, string unit, string country,
            int keySize, int validityYears)
        {
            ValidateSubject(commonName, country);
            ValidateKeySize(keySize);
            if (validityYears < 1 || validityYears > 50)
                throw new ArgumentOutOfRangeException(nameof(validityYears), "根证书有效期必须为 1 到 50 年。");

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            DateTimeOffset notAfter = notBefore.AddYears(validityYears);
            var subject = BuildSubject(commonName, organization, unit, country);

            using (RSA key = CreateRsa(keySize))
            {
                var request = new CertificateRequest(
                    subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(true, false, 0, true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature |
                        X509KeyUsageFlags.KeyCertSign |
                        X509KeyUsageFlags.CrlSign, true));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                using (X509Certificate2 generated = request.CreateSelfSigned(notBefore, notAfter))
                    return CloneWithPrivateKey(generated);
            }
        }

        internal static X509Certificate2 Issue(
            X509Certificate2 issuer,
            string commonName, string organization, string unit, string country,
            IEnumerable<string> alternativeNames,
            int keySize, int validityDays,
            bool serverAuthentication, bool clientAuthentication)
        {
            ValidateIssuer(issuer);
            ValidateSubject(commonName, country);
            ValidateKeySize(keySize);
            if (validityDays < 1 || validityDays > 3650)
                throw new ArgumentOutOfRangeException(nameof(validityDays), "签发证书有效期必须为 1 到 3650 天。");
            if (!serverAuthentication && !clientAuthentication)
                throw new ArgumentException("至少选择一个证书用途。");

            DateTimeOffset issuerNotBefore = new DateTimeOffset(issuer.NotBefore.ToUniversalTime());
            DateTimeOffset issuerNotAfter = new DateTimeOffset(issuer.NotAfter.ToUniversalTime());
            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
            if (notBefore < issuerNotBefore)
                notBefore = issuerNotBefore;
            DateTimeOffset requestedNotAfter = notBefore.AddDays(validityDays);
            DateTimeOffset notAfter = requestedNotAfter < issuerNotAfter
                ? requestedNotAfter
                : issuerNotAfter.AddMinutes(-1);
            if (notAfter <= notBefore)
                throw new InvalidOperationException("签发者证书已过期或剩余有效期不足。");

            using (RSA key = CreateRsa(keySize))
            {
                var request = new CertificateRequest(
                    BuildSubject(commonName, organization, unit, country),
                    key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(
                    new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(
                    new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
                AddUsages(request, serverAuthentication, clientAuthentication);
                AddAlternativeNames(request, alternativeNames);

                byte[] serial = CreateSerialNumber();
                try
                {
                    using (X509Certificate2 publicCertificate =
                           request.Create(issuer, notBefore, notAfter, serial))
                    using (X509Certificate2 certificateWithKey =
                           publicCertificate.CopyWithPrivateKey(key))
                        return CloneWithPrivateKey(certificateWithKey);
                }
                finally { Array.Clear(serial, 0, serial.Length); }
            }
        }

        internal static void ExportPfx(X509Certificate2 certificate, string path, string password)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("当前证书不包含私钥，无法导出 PFX。");
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("PFX 导出密码不能为空。", nameof(password));

            byte[] data = certificate.Export(X509ContentType.Pfx, password);
            try { File.WriteAllBytes(path, data); }
            finally { Array.Clear(data, 0, data.Length); }
        }

        internal static void ExportCer(X509Certificate2 certificate, string path)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Cert));
        }

        internal static void ExportPem(X509Certificate2 certificate, string path)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            byte[] der = certificate.Export(X509ContentType.Cert);
            try
            {
                string body = Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks);
                File.WriteAllText(
                    path,
                    PemBegin + Environment.NewLine + body + Environment.NewLine +
                    PemEnd + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            finally { Array.Clear(der, 0, der.Length); }
        }

        internal static bool IsCertificateAuthority(X509Certificate2 certificate)
        {
            if (certificate == null) return false;
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid != null && extension.Oid.Value == "2.5.29.19")
                {
                    var constraints = new X509BasicConstraintsExtension(extension, extension.Critical);
                    return constraints.CertificateAuthority;
                }
            }
            return false;
        }

        private static void ValidateIssuer(X509Certificate2 issuer)
        {
            if (issuer == null)
                throw new ArgumentNullException(nameof(issuer), "请选择包含私钥的签发者证书。");
            if (!issuer.HasPrivateKey)
                throw new InvalidOperationException("签发者证书不包含私钥，请导入 PFX/P12 文件。");
            if (!IsCertificateAuthority(issuer))
                throw new InvalidOperationException("签发者证书不是 CA 证书。");
            DateTime now = DateTime.UtcNow;
            if (now < issuer.NotBefore.ToUniversalTime() || now > issuer.NotAfter.ToUniversalTime())
                throw new InvalidOperationException("签发者证书当前不在有效期内。");
        }

        private static void ValidateSubject(string commonName, string country)
        {
            if (string.IsNullOrWhiteSpace(commonName))
                throw new ArgumentException("通用名称（CN）不能为空。", nameof(commonName));
            if (!string.IsNullOrWhiteSpace(country) && country.Trim().Length != 2)
                throw new ArgumentException("国家/地区代码必须是两个字母，例如 CN。", nameof(country));
        }

        private static void ValidateKeySize(int keySize)
        {
            if (keySize != 2048 && keySize != 3072 && keySize != 4096)
                throw new ArgumentOutOfRangeException(nameof(keySize), "RSA 密钥长度只支持 2048、3072 或 4096。");
        }

        private static RSA CreateRsa(int keySize)
        {
            RSA rsa = RSA.Create();
            rsa.KeySize = keySize;
            return rsa;
        }

        private static X500DistinguishedName BuildSubject(
            string commonName, string organization, string unit, string country)
        {
            var parts = new List<string> { "CN=" + Escape(commonName.Trim()) };
            if (!string.IsNullOrWhiteSpace(unit))
                parts.Add("OU=" + Escape(unit.Trim()));
            if (!string.IsNullOrWhiteSpace(organization))
                parts.Add("O=" + Escape(organization.Trim()));
            if (!string.IsNullOrWhiteSpace(country))
                parts.Add("C=" + country.Trim().ToUpperInvariant());
            return new X500DistinguishedName(string.Join(", ", parts));
        }

        private static string Escape(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void AddUsages(
            CertificateRequest request, bool serverAuthentication, bool clientAuthentication)
        {
            var usages = new OidCollection();
            if (serverAuthentication) usages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            if (clientAuthentication) usages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
        }

        private static void AddAlternativeNames(
            CertificateRequest request, IEnumerable<string> names)
        {
            if (names == null) return;
            var builder = new SubjectAlternativeNameBuilder();
            bool hasName = false;
            foreach (string rawName in names)
            {
                string name = rawName == null ? string.Empty : rawName.Trim();
                if (name.Length == 0) continue;
                IPAddress address;
                if (IPAddress.TryParse(name, out address)) builder.AddIpAddress(address);
                else builder.AddDnsName(name);
                hasName = true;
            }
            if (hasName) request.CertificateExtensions.Add(builder.Build());
        }

        private static byte[] CreateSerialNumber()
        {
            var serial = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(serial);
            serial[0] &= 0x7F;
            bool allZero = true;
            for (int i = 0; i < serial.Length; i++) allZero &= serial[i] == 0;
            if (allZero) serial[serial.Length - 1] = 1;
            return serial;
        }

        private static X509Certificate2 CloneWithPrivateKey(X509Certificate2 certificate)
        {
            string password = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            byte[] pfx = certificate.Export(X509ContentType.Pfx, password);
            try { return Import(pfx, password); }
            finally { Array.Clear(pfx, 0, pfx.Length); }
        }

        private static X509Certificate2 Import(byte[] data, string password)
        {
            try
            {
                return new X509Certificate2(
                    data, password,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException)
            {
                try
                {
                    // 部分旧版 Mono 不支持 EphemeralKeySet。
                    return new X509Certificate2(data, password, X509KeyStorageFlags.Exportable);
                }
                catch (CryptographicException)
                {
                    // 无用户配置文件的 Windows 批处理环境需要临时使用机器密钥容器。
                    return new X509Certificate2(
                        data, password,
                        X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
                }
            }
        }

        private static byte[] DecodePem(string pem)
        {
            int begin = pem.IndexOf(PemBegin, StringComparison.Ordinal);
            if (begin < 0) throw new FormatException("PEM 中没有 CERTIFICATE 区块。");
            int contentStart = begin + PemBegin.Length;
            int end = pem.IndexOf(PemEnd, contentStart, StringComparison.Ordinal);
            if (end < 0) throw new FormatException("PEM 中的 CERTIFICATE 区块不完整。");
            string body = pem.Substring(contentStart, end - contentStart);
            var compact = new StringBuilder(body.Length);
            foreach (char character in body)
                if (!char.IsWhiteSpace(character)) compact.Append(character);
            return Convert.FromBase64String(compact.ToString());
        }
    }
}
