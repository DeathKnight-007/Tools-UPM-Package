using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UnityEditor;
using UnityEngine;

namespace DeathKnight.Net.Editor
{
    internal sealed class CertificateToolWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "导入与读取", "创建根证书", "签名与下发" };
        private static readonly int[] KeySizes = { 2048, 3072, 4096 };
        private static readonly string[] KeySizeLabels = { "RSA 2048", "RSA 3072", "RSA 4096" };

        private int _selectedTab;
        private Vector2 _scrollPosition;
        private string _statusMessage;
        private MessageType _statusType = MessageType.Info;

        private X509Certificate2 _importedCertificate;
        private string _importPath = string.Empty;
        private string _importPassword = string.Empty;

        private X509Certificate2 _rootCertificate;
        private string _rootCommonName = "DeathKnight Development Root CA";
        private string _rootOrganization = "DeathKnight";
        private string _rootOrganizationalUnit = "Development";
        private string _rootCountryCode = "CN";
        private int _rootKeySizeIndex;
        private int _rootValidityYears = 10;
        private string _rootExportPassword = string.Empty;
        private string _rootExportPasswordConfirmation = string.Empty;

        private X509Certificate2 _issuerCertificate;
        private bool _useGeneratedRootAsIssuer;
        private string _issuerPath = string.Empty;
        private string _issuerPassword = string.Empty;
        private X509Certificate2 _issuedCertificate;
        private string _issuedCommonName = "localhost";
        private string _issuedOrganization = "DeathKnight";
        private string _issuedOrganizationalUnit = "Development";
        private string _issuedCountryCode = "CN";
        private string _subjectAlternativeNames = "localhost, 127.0.0.1, ::1";
        private int _issuedKeySizeIndex;
        private int _issuedValidityDays = 365;
        private bool _serverAuthentication = true;
        private bool _clientAuthentication;
        private string _issuedExportPassword = string.Empty;
        private string _issuedExportPasswordConfirmation = string.Empty;

        private X509Certificate2 ActiveIssuer
        {
            get { return _useGeneratedRootAsIssuer ? _rootCertificate : _issuerCertificate; }
        }

        [MenuItem("Tools/DeathKnight/证书工具")]
        private static void OpenWindow()
        {
            CertificateToolWindow window = GetWindow<CertificateToolWindow>();
            window.titleContent = new GUIContent("证书工具");
            window.minSize = new Vector2(760f, 620f);
            window.Show();
        }

        private void OnDisable()
        {
            Dispose(ref _importedCertificate);
            Dispose(ref _rootCertificate);
            Dispose(ref _issuerCertificate);
            Dispose(ref _issuedCertificate);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("DeathKnight 证书工具", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "查看证书、创建自签名根证书，并使用 CA 私钥签发服务器或客户端证书。",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8f);

            int newTab = GUILayout.Toolbar(_selectedTab, Tabs, GUILayout.Height(28f));
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                _scrollPosition = Vector2.zero;
                _statusMessage = string.Empty;
            }

            EditorGUILayout.Space(6f);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            if (_selectedTab == 0) DrawImportPage();
            else if (_selectedTab == 1) DrawRootPage();
            else DrawIssuePage();
            EditorGUILayout.Space(8f);
            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            EditorGUILayout.EndScrollView();
        }

        private void DrawImportPage()
        {
            DrawTitle("1. 导入并读取证书");
            EditorGUILayout.HelpBox(
                "支持 PFX/P12（可含私钥）、DER 格式 CER/CRT，以及包含 CERTIFICATE 区块的 PEM。PEM 私钥不会被导入。",
                MessageType.Info);
            DrawPathField("证书文件", _importPath, SelectImportCertificate);
            _importPassword = EditorGUILayout.PasswordField(
                new GUIContent("证书密码", "CER/CRT/PEM 无需填写；PFX/P12 按需填写。"),
                _importPassword);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导入并读取", GUILayout.Height(28f))) ImportCertificate();
            using (new EditorGUI.DisabledScope(_importedCertificate == null))
            {
                if (GUILayout.Button("清除", GUILayout.Width(100f), GUILayout.Height(28f)))
                {
                    Dispose(ref _importedCertificate);
                    _importPath = string.Empty;
                    _statusMessage = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_importedCertificate == null) return;
            EditorGUILayout.Space(10f);
            DrawCertificate(_importedCertificate, "证书信息");
            EditorGUILayout.BeginHorizontal();
            DrawPublicExportButtons(_importedCertificate, SuggestedName(_importedCertificate));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRootPage()
        {
            DrawTitle("2. 创建并导出根证书");
            EditorGUILayout.HelpBox(
                "根证书私钥拥有签发能力。请只在受控开发环境使用，并通过强密码保护导出的 PFX 文件。",
                MessageType.Warning);
            _rootCommonName = EditorGUILayout.TextField("通用名称（CN）", _rootCommonName);
            _rootOrganization = EditorGUILayout.TextField("组织（O）", _rootOrganization);
            _rootOrganizationalUnit = EditorGUILayout.TextField("组织单位（OU）", _rootOrganizationalUnit);
            _rootCountryCode = EditorGUILayout.TextField("国家/地区（C）", _rootCountryCode);
            _rootKeySizeIndex = EditorGUILayout.Popup("密钥长度", _rootKeySizeIndex, KeySizeLabels);
            _rootValidityYears = EditorGUILayout.IntSlider("有效期（年）", _rootValidityYears, 1, 50);
            DrawPasswordFields(
                ref _rootExportPassword, ref _rootExportPasswordConfirmation);

            if (GUILayout.Button("生成自签名根证书", GUILayout.Height(30f))) GenerateRoot();
            if (_rootCertificate == null) return;

            EditorGUILayout.Space(10f);
            DrawCertificate(_rootCertificate, "已生成的根证书");
            DrawAllExportButtons(
                _rootCertificate, SuggestedName(_rootCertificate),
                _rootExportPassword, _rootExportPasswordConfirmation);
        }

        private void DrawIssuePage()
        {
            DrawTitle("3. 使用 CA 签名并下发证书");
            EditorGUILayout.LabelField("签发者（CA）", EditorStyles.boldLabel);
            DrawPathField("CA 的 PFX/P12", _issuerPath, SelectIssuerCertificate);
            _issuerPassword = EditorGUILayout.PasswordField("CA 证书密码", _issuerPassword);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导入 CA 证书", GUILayout.Height(28f))) ImportIssuer();
            using (new EditorGUI.DisabledScope(_rootCertificate == null))
            {
                if (GUILayout.Button("使用已生成的根证书", GUILayout.Height(28f)))
                {
                    _useGeneratedRootAsIssuer = true;
                    SetStatus("已选择第二页生成的根证书作为签发者。", MessageType.Info);
                }
            }
            EditorGUILayout.EndHorizontal();

            X509Certificate2 issuer = ActiveIssuer;
            if (issuer != null)
            {
                EditorGUILayout.Space(6f);
                DrawCertificate(issuer, "当前签发者");
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("待签发证书", EditorStyles.boldLabel);
            _issuedCommonName = EditorGUILayout.TextField("通用名称（CN）", _issuedCommonName);
            _issuedOrganization = EditorGUILayout.TextField("组织（O）", _issuedOrganization);
            _issuedOrganizationalUnit = EditorGUILayout.TextField("组织单位（OU）", _issuedOrganizationalUnit);
            _issuedCountryCode = EditorGUILayout.TextField("国家/地区（C）", _issuedCountryCode);
            _subjectAlternativeNames = EditorGUILayout.TextField(
                new GUIContent("SAN", "用逗号、分号或换行分隔 DNS 名称和 IP 地址。"),
                _subjectAlternativeNames);
            _issuedKeySizeIndex = EditorGUILayout.Popup("密钥长度", _issuedKeySizeIndex, KeySizeLabels);
            _issuedValidityDays = EditorGUILayout.IntSlider("有效期（天）", _issuedValidityDays, 1, 3650);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("证书用途");
            _serverAuthentication = GUILayout.Toggle(_serverAuthentication, "服务器身份验证");
            _clientAuthentication = GUILayout.Toggle(_clientAuthentication, "客户端身份验证");
            EditorGUILayout.EndHorizontal();
            DrawPasswordFields(
                ref _issuedExportPassword, ref _issuedExportPasswordConfirmation);

            using (new EditorGUI.DisabledScope(issuer == null))
            {
                if (GUILayout.Button("签名并生成证书", GUILayout.Height(30f))) GenerateIssued();
            }
            if (_issuedCertificate == null) return;

            EditorGUILayout.Space(10f);
            DrawCertificate(_issuedCertificate, "已签发证书");
            DrawAllExportButtons(
                _issuedCertificate, SuggestedName(_issuedCertificate),
                _issuedExportPassword, _issuedExportPasswordConfirmation);
        }

        private void SelectImportCertificate()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "选择证书", DefaultDirectory(),
                new[] { "证书文件", "pfx,p12,cer,crt,pem", "所有文件", "*" });
            if (!string.IsNullOrEmpty(path)) _importPath = path;
        }

        private void SelectIssuerCertificate()
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "选择包含私钥的 CA 证书", DefaultDirectory(),
                new[] { "PFX/P12", "pfx,p12", "所有文件", "*" });
            if (!string.IsNullOrEmpty(path))
            {
                _issuerPath = path;
                _useGeneratedRootAsIssuer = false;
            }
        }

        private void ImportCertificate()
        {
            Execute("证书导入成功。", delegate
            {
                Replace(ref _importedCertificate,
                    CertificateUtility.Load(_importPath, _importPassword));
            });
        }

        private void ImportIssuer()
        {
            Execute("CA 证书导入成功。", delegate
            {
                X509Certificate2 certificate = CertificateUtility.Load(_issuerPath, _issuerPassword);
                if (!certificate.HasPrivateKey || !CertificateUtility.IsCertificateAuthority(certificate))
                {
                    certificate.Dispose();
                    throw new InvalidOperationException("请选择包含私钥的 CA 类型 PFX/P12 证书。");
                }
                Replace(ref _issuerCertificate, certificate);
                _useGeneratedRootAsIssuer = false;
            });
        }

        private void GenerateRoot()
        {
            Execute("根证书生成成功。请导出 PFX 私钥证书和需要分发的公开证书。", delegate
            {
                ValidatePasswords(_rootExportPassword, _rootExportPasswordConfirmation);
                Replace(ref _rootCertificate, CertificateUtility.CreateRoot(
                    _rootCommonName, _rootOrganization, _rootOrganizationalUnit,
                    _rootCountryCode, KeySizes[_rootKeySizeIndex], _rootValidityYears));
                Dispose(ref _issuedCertificate);
            });
        }

        private void GenerateIssued()
        {
            Execute("证书签发成功。请导出 PFX 交付使用，并按需分发 CER/PEM 公开证书。", delegate
            {
                ValidatePasswords(_issuedExportPassword, _issuedExportPasswordConfirmation);
                Replace(ref _issuedCertificate, CertificateUtility.Issue(
                    ActiveIssuer,
                    _issuedCommonName, _issuedOrganization, _issuedOrganizationalUnit,
                    _issuedCountryCode, ParseNames(_subjectAlternativeNames),
                    KeySizes[_issuedKeySizeIndex], _issuedValidityDays,
                    _serverAuthentication, _clientAuthentication));
            });
        }

        private void DrawAllExportButtons(
            X509Certificate2 certificate, string suggestedName,
            string password, string confirmation)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("导出 PFX（含私钥）", GUILayout.Height(26f)))
                ExportPfx(certificate, suggestedName, password, confirmation);
            DrawPublicExportButtons(certificate, suggestedName);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPublicExportButtons(X509Certificate2 certificate, string suggestedName)
        {
            if (GUILayout.Button("导出 CER（公开）", GUILayout.Height(26f)))
            {
                string path = EditorUtility.SaveFilePanel(
                    "导出 CER", DefaultDirectory(), suggestedName, "cer");
                if (!string.IsNullOrEmpty(path))
                    Execute("CER 已导出。", delegate
                    {
                        CertificateUtility.ExportCer(certificate, path);
                        AssetDatabase.Refresh();
                    });
            }
            if (GUILayout.Button("导出 PEM（公开）", GUILayout.Height(26f)))
            {
                string path = EditorUtility.SaveFilePanel(
                    "导出 PEM", DefaultDirectory(), suggestedName, "pem");
                if (!string.IsNullOrEmpty(path))
                    Execute("PEM 已导出。", delegate
                    {
                        CertificateUtility.ExportPem(certificate, path);
                        AssetDatabase.Refresh();
                    });
            }
        }

        private void ExportPfx(
            X509Certificate2 certificate, string suggestedName,
            string password, string confirmation)
        {
            try { ValidatePasswords(password, confirmation); }
            catch (Exception exception)
            {
                SetStatus(exception.Message, MessageType.Error);
                return;
            }
            string path = EditorUtility.SaveFilePanel(
                "导出 PFX", DefaultDirectory(), suggestedName, "pfx");
            if (!string.IsNullOrEmpty(path))
                Execute("PFX 已导出。", delegate
                {
                    CertificateUtility.ExportPfx(certificate, path, password);
                    AssetDatabase.Refresh();
                });
        }

        private static void DrawPathField(string label, string value, Action browse)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(value) ? "未选择" : value,
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("浏览…", GUILayout.Width(80f))) browse();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawPasswordFields(ref string password, ref string confirmation)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("PFX 导出保护", EditorStyles.boldLabel);
            password = EditorGUILayout.PasswordField("导出密码", password);
            confirmation = EditorGUILayout.PasswordField("确认密码", confirmation);
        }

        private static void DrawCertificate(X509Certificate2 certificate, string title)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            DrawValue("主题", certificate.Subject);
            DrawValue("签发者", certificate.Issuer);
            DrawValue("序列号", certificate.SerialNumber);
            DrawValue("指纹（SHA-1）", certificate.Thumbprint);
            DrawValue("生效时间", certificate.NotBefore.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            DrawValue("失效时间", certificate.NotAfter.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            DrawValue("签名算法", FormatOid(certificate.SignatureAlgorithm));
            DrawValue("公钥", FormatPublicKey(certificate));
            DrawValue("CA 证书", CertificateUtility.IsCertificateAuthority(certificate) ? "是" : "否");
            DrawValue("包含私钥", certificate.HasPrivateKey ? "是" : "否");
            DrawValue("当前状态", Validity(certificate));
            EditorGUILayout.EndVertical();
        }

        private static void DrawValue(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUILayout.SelectableLabel(
                value ?? string.Empty, EditorStyles.wordWrappedLabel,
                GUILayout.MinHeight(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        private static string FormatOid(Oid oid)
        {
            if (oid == null) return "未知";
            return string.IsNullOrEmpty(oid.FriendlyName)
                ? oid.Value : oid.FriendlyName + " (" + oid.Value + ")";
        }

        private static string FormatPublicKey(X509Certificate2 certificate)
        {
            string algorithm = FormatOid(certificate.PublicKey.Oid);
            try
            {
                using (RSA rsa = certificate.GetRSAPublicKey())
                    return rsa == null ? algorithm : algorithm + ", " + rsa.KeySize + " bit";
            }
            catch (CryptographicException) { return algorithm; }
        }

        private static string Validity(X509Certificate2 certificate)
        {
            DateTime now = DateTime.Now;
            if (now < certificate.NotBefore) return "尚未生效";
            return now > certificate.NotAfter ? "已过期" : "有效";
        }

        private static void DrawTitle(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(3f);
        }

        private void Execute(string success, Action action)
        {
            try
            {
                action();
                SetStatus(success, MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, MessageType.Error);
                Debug.LogException(exception);
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusType = type;
            Repaint();
        }

        private static void ValidatePasswords(string password, string confirmation)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("PFX 导出密码不能为空。");
            if (!string.Equals(password, confirmation, StringComparison.Ordinal))
                throw new InvalidOperationException("两次输入的 PFX 导出密码不一致。");
        }

        private static IEnumerable<string> ParseNames(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? Array.Empty<string>()
                : value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string SuggestedName(X509Certificate2 certificate)
        {
            string value = certificate.GetNameInfo(X509NameType.SimpleName, false);
            if (string.IsNullOrWhiteSpace(value)) value = "certificate";
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static string DefaultDirectory()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static void Replace(ref X509Certificate2 destination, X509Certificate2 replacement)
        {
            Dispose(ref destination);
            destination = replacement;
        }

        private static void Dispose(ref X509Certificate2 certificate)
        {
            if (certificate == null) return;
            certificate.Dispose();
            certificate = null;
        }
    }
}
