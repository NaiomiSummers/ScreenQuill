# Distribution audit

The portable package contains first-party source, scripts, and SopRecorder.exe. It uses Windows/.NET Framework assemblies supplied by Windows (System, System.Core, System.Drawing, System.Windows.Forms, System.IO.Compression and System.IO.Compression.FileSystem); these runtimes are prerequisites and are not redistributed. Win32 calls use operating-system services. No third-party DLLs, fonts or external runtime packages are bundled. Prior test captures/exports have been removed from the distribution; new fictional QA evidence is kept outside the package. No copied snippet carrying a separate attribution was identified in source.

This is an inventory of identified shipping components, not a license grant for excluded material. Preserve each applicable notice when repackaging.
