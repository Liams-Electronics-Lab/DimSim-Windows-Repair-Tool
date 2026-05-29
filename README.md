# DimSim-Windows-Repair-Tool

DimSim Windows Repair is a graphical tool for repairing offline Windows installations. It allows you to perform system health checks, restore corrupted system files, manage startup entries, edit the Boot Configuration Data, install or uninstall Windows updates, and add Features on Demand, all without booting into the target operating system.

<div align="center">
  <img width="350" height="350" src="https://github.com/user-attachments/assets/a1f54b80-d67b-44e9-b3c5-24a69901199e" />
</div>

## Features

* System repair using an external WIM/ESD image or the local component store (WinSxS)
* Revert pending updates, may resolve boot loops caused by incomplete updates
* Extract the Windows product key from an offline registry hive
* Startup applications manager (for the offline Windows install)


* BCD (Boot Configuration Data) management:```Locate the BCD store on BIOS or EFI systems + View, edit, and delete boot entries + Change boot menu timeout + Backup and restore the BCD store + Automatically rebuild the BCD using bcdboot```


* Windows Update manager for the offline system ```List installed updates, security updates, rollups, and hotfixes + Uninstall or reinstall an update (re‑downloads from Microsoft Update Catalog) + Manually install a KB by downloading it from the Update Catalog (requires PowerShell module MSCatalogLTS) + install .cab packages manually ```

## Requirements

* Windows operating system (the tool runs on Windows 10/11)

* Administrator privileges 

* .NET Framework 4.7.2 or later (or .NET Core/.NET 5+ with Windows Compatibility Pack)

### For KB download functionality:

* PowerShell 5.1 or later

* MSCatalogLTS module installed in AllUsers scope
 ```
Install-Module -Name MSCatalogLTS -Scope AllUsers -Force
Get-ChildItem 'C:\Program Files\WindowsPowerShell\Modules\MSCatalogLTS' -Recurse | Unblock-File
```
## How to Use


1. Launch the application as Administrator.

2. Select the target drive (non‑system drive where the offline Windows resides).

3. Click Check OS to read the version, edition, build, and architecture.


<img width="786" height="913" alt="Screenshot 2026-05-29 155540" src="https://github.com/user-attachments/assets/e9c2ac38-57aa-48af-94d5-587d98d85dae" />


### Depending on what you want to do:

``Repair system files – either:``
a) Provide a Windows installation ISO/WIM/ESD, scan it, choose an edition, then click Repair (external source).
b) Check Use local component store (no external WIM/ESD required) and click Repair (uses the offline system’s own WinSxS).

If local store is used, you may also check Attempt to use WSUS / Windows Update to allow DISM to contact Windows Update.

``Revert pending updates `` click the orange button. This can resolve boot loops caused by failed updates.

``Manage updates `` opens a separate window where you can view installed updates, uninstall or reinstall them, or download and install a specific KB number.

``Manage startup apps `` lists all startup entries (machine and per‑user). You can enable or disable any entry.

``BCD Management `` view boot entries, edit them, change timeout, backup/restore, or rebuild the BCD store from the offline Windows directory.

``Extract Product Key `` displays the product key stored in the registry (if any).

``Features on Demand `` from the Update Manager window, select an FoD ISO or folder, then click Open Feature Selector to install optional features.


<img width="1086" height="693" alt="Screenshot 2026-05-29 175646" src="https://github.com/user-attachments/assets/f0b79ce6-b8ab-4ff1-bc61-80a512668715" />
<img width="431" height="244" alt="Screenshot 2026-05-29 175548" src="https://github.com/user-attachments/assets/69c01cc9-2cfb-4db7-812a-e76bd99ac7c9" />
<img width="1086" height="693" alt="Screenshot 2026-05-29 175447" src="https://github.com/user-attachments/assets/c8c244a2-010a-4134-99ef-432a63751455" />
<img width="786" height="873" alt="Screenshot 2026-05-29 153334" src="https://github.com/user-attachments/assets/70c63485-b76b-44ab-a32f-c570850056ee" />








Made by Liams Electronics Lab
