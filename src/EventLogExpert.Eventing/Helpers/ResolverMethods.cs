// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Helpers;

internal static class ResolverMethods
{
    /// <summary>Listing common error codes to prevent Exception allocation and to drop HResult value from Message result</summary>
    internal static string GetErrorMessage(uint hResult) =>
        hResult switch
        {
            0x00000000 => "The operation completed successfully.",

            // Generic HResult Codes
            // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/705fb797-2175-4a90-b5a3-3918024b10b8
            0x80004001 => "Not implemented",
            0x80004002 => "No such interface supported",
            0x80004003 => "Invalid pointer",
            0x80004004 => "Operation aborted",
            0x80004005 => "Unspecified error",
            0x8000FFFF => "Catastrophic failure",

            0x80070002 => "The system cannot find the file specified",
            0x80070005 => "General access denied error",
            0x80070006 => "Invalid handle",
            0x8007000E => "Ran out of memory",
            0x80070032 => "The request is not supported",
            0x80070057 => "One or more arguments are invalid",
            0x80070520 => "A specific logon session does not exist. It may already have been terminated",
            0x80070718 => "Not enough quota is available to process this command",

            // AppX
            0x80073CF0 => "Package could not be opened",
            0x80073CF1 => "Package was not found",
            0x80073CF2 => "Package data is invalid",
            0x80073CF3 => "Package failed updates, dependency or conflict validation",
            0x80073CF4 => "There is not enough disk space on your computer. Please free up some space and try again",
            0x80073CF5 => "There was a problem downloading your product",
            0x80073CF6 => "Package could not be registered",
            0x80073CF7 => "Package could not be unregistered",
            0x80073CF8 => "User cancelled the install request",
            0x80073CF9 => "Install failed. Please contact your software vendor",
            0x80073CFA => "Removal failed. Please contact your software vendor",
            0x80073CFB => "The provided package is already installed, and reinstallation of the package was blocked. Check the AppXDeployment-Server event log for details",
            0x80073CFC => "The application cannot be started. Try reinstalling the application to fix the problem",
            0x80073CFD => "A Prerequisite for an install could not be satisfied",
            0x80073CFE => "The package repository is corrupted",
            0x80073CFF => "To install this application you need either a Windows developer license or a sideloading-enabled system",
            0x80073D00 => "The application cannot be started because it is currently updating",
            0x80073D01 => "The package deployment operation is blocked by AllowDeploymentInSpecialProfiles policy",
            0x80073D02 => "The package could not be installed because resources it modifies are currently in use",
            0x80073D03 => "The package could not be recovered because necessary data for recovery have been corrupted",
            0x80073D04 => "The signature is invalid. To register in developer mode, AppxSignature.p7x and AppxBlockMap.xml must be valid or should not be present",
            0x80073D05 => "An error occurred while deleting the existing applicationdata store locations",
            0x80073D06 => "The package could not be installed because a higher version of this package is already installed",
            0x80073D0A => "The package could not be installed because the Windows Firewall service is not running. Enable the Windows Firewall service and try again",
            0x80073D19 => "An error occurred because a user was logged off",

            // AppModel
            0x80073D54 => "The process has no package identity",
            0x80073D55 => "The package runtime information is corrupted. Try uninstalling and reinstalling all AppX packages",
            0x80073D56 => "The package identity is corrupted. Try uninstalling and reinstalling all AppX package",

            // AppX StateManager
            0x80073DB8 => "Loading the state store failed",
            0x80073DB9 => "Retrieving the state version for the application failed",
            0x80073DBA => "Setting the state version for the application failed",
            0x80073DBB => "Resetting the structured state of the application failed",
            0x80073DBC => "State Manager failed to open the container",
            0x80073DBD => "State Manager failed to create the container",
            0x80073DBE => "State Manager failed to delete the container",
            0x80073DBF => "State Manager failed to read the setting",
            0x80073DC0 => "State Manager failed to write the setting",
            0x80073DC1 => "State Manager failed to delete the setting",
            0x80073DC2 => "State Manager failed to query the setting",
            0x80073DC3 => "State Manager failed to read the composite setting",
            0x80073DC4 => "State Manager failed to write the composite setting",
            0x80073DC5 => "State Manager failed to enumerate the containers",
            0x80073DC6 => "State Manager failed to enumerate the settings",
            0x80073DC7 => "The size of the state manager composite setting value has exceeded the limit",
            0x80073DC8 => "The size of the state manager setting value has exceeded the limit",
            0x80073DC9 => "The length of the state manager setting name has exceeded the limit",
            0x80073DCA => "The length of the state manager container name has exceeded the limit",

            // Generic NT Status Codes
            // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e60bebca55
            0xC0000002 => "STATUS_NOT_IMPLEMENTED",
            0xC0000005 => "STATUS_ACCESS_VIOLATION",
            0xC0000194 => "APPLICATION_HANG",
            0xC000027B => "STATUS_STOWED_EXCEPTION",
            0xC0000409 => "STATUS_STACK_BUFFER_OVERRUN",

            _ => Marshal.GetExceptionForHR((int)hResult)?.Message ?? hResult.ToString()
        };
}
