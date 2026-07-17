// FIX: Create a clean separation between Settings UI display and Settings processing
// This file contains the methods to properly fix the Settings button issue

// 1. NEW: Method that ONLY displays the Settings UI dialog (what button click handlers should call)
void ShowSettingsDialog()
{
    using (var dlg = new SettingsDialog(providerManager, configPath, Tr))
    {
        dlg.ShowDialog(this);
    }
}

// 2. EXISTING but refactored: Method that processes settings after dialog closes
// This method should be renamed to ApplySettings() to clarify its purpose
void ShowSettings()
{
    using (var dlg = new SettingsDialog(providerManager, configPath, Tr, UpdateLanguage, uiLanguage))
    {
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            string oldLanguage = uiLanguage;

            var sel = dlg.SelectedModel;
            if (sel != null)
            {
                provider = sel.Endpoint;
                model = sel.Id;
                if (sel.Endpoint == "Local") localModel = sel.Id;
                providerManager.SelectModel(sel);
            }

            // Update all settings that were modified in the UI
            apiKey = dlg.GeminiKey;
            openRouterApiKey = dlg.OpenRouterKey;
            localApiUrl = dlg.LocalApiUrl;
            embeddingModel = providerManager.EmbeddingModel;
            providerManager.GeminiKey = dlg.GeminiKey;
            providerManager.OpenRouterKey = dlg.OpenRouterKey;
            providerManager.LocalApiUrl = dlg.LocalApiUrl;
            providerManager.CustomApiUrl = dlg.CustomApiUrl;
            providerManager.CustomApiKey = dlg.CustomApiKey;

            // Update language if needed
            if (!string.Equals(oldLanguage, uiLanguage, StringComparison.OrdinalIgnoreCase))
                RebuildUiForLanguage();

            // Save configuration
            SaveConfig();
            if (modelLabel != null) modelLabel.Text = CurrentModelLabel();
            ApplyHotkeyRegistration();
        }
    }
}

// 3. IMPROVED: Combined method that shows dialog and processes settings
void ShowSettingsAndApply()
{
    ShowSettings();
}
