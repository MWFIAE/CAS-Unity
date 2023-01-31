﻿//
//  Clever Ads Solutions Unity Plugin
//
//  Copyright © 2023 CleverAdsSolutions. All rights reserved.
//

namespace CAS
{
    public interface IAdsPreset
    {
        int defaultBannerRefresh { get; }
        int defaultInterstitialInterval { get; }
        bool defaultDebugModeEnabled { get; }
        bool defaultIOSTrackLocationEnabled { get; }
        bool defaultInterstitialWhenNoRewardedAd { get; }
        AdFlags defaultAllowedFormats { get; }
        Audience defaultAudienceTagged { get; }
        LoadingManagerMode defaultLoadingMode { get; }

        int managersCount { get; }
        string GetManagerId( int index );
        int IndexOfManagerId( string id );
        bool IsTestAdMode();
    }
}