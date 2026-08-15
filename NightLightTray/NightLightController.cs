using System;
using Microsoft.Win32;

namespace NightLightTray
{
    public static class NightLightController
    {
        private const string StateKeyPath =
            @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

        public static bool IsAvailable()
        {
            return ReadData(StateKeyPath) != null;
        }

        public static bool GetEnabled()
        {
            byte[] data = ReadData(StateKeyPath);
            return data != null && data.Length > 18 && data[18] == 0x15;
        }

        public static void SetEnabled(bool enabled)
        {
            byte[] data = ReadData(StateKeyPath);
            if (data == null)
            {
                throw new InvalidOperationException("Night Light state registry key not found.");
            }

            bool currentlyEnabled = data.Length > 18 && data[18] == 0x15;
            if (currentlyEnabled == enabled)
            {
                return;
            }

            if (enabled)
            {
                byte[] newData = new byte[43];
                Array.Copy(data, 0, newData, 0, Math.Min(22, data.Length));
                if (data.Length > 23)
                {
                    int copyLength = Math.Min(data.Length - 23, 18);
                    Array.Copy(data, 23, newData, 25, copyLength);
                }
                newData[18] = 0x15;
                newData[23] = 0x10;
                newData[24] = 0x00;
                BumpTimestamp(newData);
                WriteData(StateKeyPath, newData);
            }
            else
            {
                byte[] newData = new byte[41];
                Array.Copy(data, 0, newData, 0, Math.Min(22, data.Length));
                if (data.Length > 25)
                {
                    int copyLength = Math.Min(data.Length - 25, 16);
                    Array.Copy(data, 25, newData, 23, copyLength);
                }
                newData[18] = 0x13;
                BumpTimestamp(newData);
                WriteData(StateKeyPath, newData);
            }
        }

        private static void BumpTimestamp(byte[] data)
        {
            for (int i = 10; i < 15 && i < data.Length; i++)
            {
                if (data[i] != 0xFF)
                {
                    data[i] = (byte)((data[i] + 1) & 0xFF);
                    break;
                }
            }
        }

        private static byte[] ReadData(string keyPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
                {
                    return key?.GetValue("Data") as byte[];
                }
            }
            catch
            {
                return null;
            }
        }

        private static void WriteData(string keyPath, byte[] data)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Registry key not found: " + keyPath);
                }
                key.SetValue("Data", data, RegistryValueKind.Binary);
            }
        }
    }
}
