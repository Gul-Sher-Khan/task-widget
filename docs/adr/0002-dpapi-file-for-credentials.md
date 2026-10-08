# Credentials in a DPAPI-encrypted file, not Credential Manager

The ChatGPT token record, the only secret, lives in a single per-user DPAPI-encrypted file under `%LOCALAPPDATA%\TaskWidget\`, not in Windows Credential Manager. Credential Manager caps a blob at 2,560 bytes, but the ChatGPT access, refresh and ID tokens together exceed that (~3.4 KB measured). The Sign in with ChatGPT terms also require token storage to stay local and under the user's control. Non-secret settings stay in the ordinary settings file.
