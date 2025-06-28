mergeInto(LibraryManager.library, {
    FirebaseSignUp: function (username, password) {
        var usernameStr = UTF8ToString(username);
        var passwordStr = UTF8ToString(password);
        console.log('JS Bridge received from Unity:', { username: usernameStr, password: passwordStr });
        if (typeof window.FirebaseSignUp === 'function') {
            window.FirebaseSignUp(usernameStr, passwordStr);
        } else {
            console.error('window.FirebaseSignUp is not defined');
        }
    },
    FirebaseSignIn: function (username, password) {
        var usernameStr = UTF8ToString(username);
        var passwordStr = UTF8ToString(password);
        if (typeof window.FirebaseSignIn === 'function') {
            window.FirebaseSignIn(usernameStr, passwordStr);
        }
    },
    FirebaseSaveCrowns: function (uid, crowns, gameMode) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseSaveCrowns === 'function') {
            window.FirebaseSaveCrowns(uidStr, crowns, gameModeStr);
        }
    },
    FirebaseLoadCrowns: function (uid, gameMode) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseLoadCrowns === 'function') {
            window.FirebaseLoadCrowns(uidStr, gameModeStr);
        }
    },
    FirebaseLoadLeaderboard: function (gameMode, limit) {
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseLoadLeaderboard === 'function') {
            window.FirebaseLoadLeaderboard(gameModeStr, limit);
        }
    },
    FirebaseSendFriendRequest: function (fromUid, toUsername, callbackObjectName) {
        var fromUidStr = UTF8ToString(fromUid);
        var toUsernameStr = UTF8ToString(toUsername);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseSendFriendRequest === 'function') {
            window.FirebaseSendFriendRequest(fromUidStr, toUsernameStr, callbackObjectNameStr);
        }
    },
    FirebaseAcceptFriendRequest: function (uid, friendUid, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var friendUidStr = UTF8ToString(friendUid);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseAcceptFriendRequest === 'function') {
            window.FirebaseAcceptFriendRequest(uidStr, friendUidStr, callbackObjectNameStr);
        }
    },
    FirebaseRejectFriendRequest: function (uid, friendUid, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var friendUidStr = UTF8ToString(friendUid);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseRejectFriendRequest === 'function') {
            window.FirebaseRejectFriendRequest(uidStr, friendUidStr, callbackObjectNameStr);
        }
    },
    FirebaseLoadFriends: function (uid, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseLoadFriends === 'function') {
            window.FirebaseLoadFriends(uidStr, callbackObjectNameStr);
        }
    },
    FirebaseCreateParty: function (uid, partyId, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var partyIdStr = UTF8ToString(partyId);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseCreateParty === 'function') {
            window.FirebaseCreateParty(uidStr, partyIdStr, callbackObjectNameStr);
        }
    },
    FirebaseInviteToParty: function (partyId, friendUid, callbackObjectName) {
        var partyIdStr = UTF8ToString(partyId);
        var friendUidStr = UTF8ToString(friendUid);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseInviteToParty === 'function') {
            window.FirebaseInviteToParty(partyIdStr, friendUidStr, callbackObjectNameStr);
        }
    },
    FirebaseLeaveParty: function (uid, partyId, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var partyIdStr = UTF8ToString(partyId);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseLeaveParty === 'function') {
            window.FirebaseLeaveParty(uidStr, partyIdStr, callbackObjectNameStr);
        }
    },
    FirebaseCheckOnlineStatus: function (friendUid, callbackObjectName) {
        var friendUidStr = UTF8ToString(friendUid);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseCheckOnlineStatus === 'function') {
            window.FirebaseCheckOnlineStatus(friendUidStr, callbackObjectNameStr);
        }
    },
    FirebaseLoadPartyMembers: function (partyId, callbackObjectName) {
        var partyIdStr = UTF8ToString(partyId);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseLoadPartyMembers === 'function') {
            window.FirebaseLoadPartyMembers(partyIdStr, callbackObjectNameStr);
        }
    },
    FirebaseSignOut: function () {
        if (typeof window.FirebaseSignOut === 'function') {
            window.FirebaseSignOut();
        }
    },
    FirebaseLinkEmail: function (uid, email, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var emailStr = UTF8ToString(email);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseLinkEmail === 'function') {
            window.FirebaseLinkEmail(uidStr, emailStr, callbackObjectNameStr);
        }
    },
    FirebaseSendPasswordReset: function (email, callbackObjectName) {
        var emailStr = UTF8ToString(email);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseSendPasswordReset === 'function') {
            window.FirebaseSendPasswordReset(emailStr, callbackObjectNameStr);
        }
    },
    FirebaseGetEmail: function (uid, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseGetEmail === 'function') {
            window.FirebaseGetEmail(uidStr, callbackObjectNameStr);
        }
    },
    FirebaseSendVerificationEmail: function (callbackObjectName) {
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseSendVerificationEmail === 'function') {
            window.FirebaseSendVerificationEmail(callbackObjectNameStr);
        }
    },
    FirebaseJoinParty: function (uid, partyId, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var partyIdStr = UTF8ToString(partyId);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseJoinParty === 'function') {
            window.FirebaseJoinParty(uidStr, partyIdStr, callbackObjectNameStr);
        }
    },
    FirebaseGetVerificationStatus: function (callbackObjectName) {
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseGetVerificationStatus === 'function') {
            window.FirebaseGetVerificationStatus(callbackObjectNameStr);
        }
    },
    FirebaseSaveBrightMatter: function (uid, brightMatter, gameMode) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseSaveBrightMatter === 'function') {
            window.FirebaseSaveBrightMatter(uidStr, brightMatter, gameModeStr);
        } else {
            console.error('FirebaseSaveBrightMatter is not defined');
        }
    },
    FirebaseLoadBrightMatter: function (uid, gameMode) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseLoadBrightMatter === 'function') {
            window.FirebaseLoadBrightMatter(uidStr, gameModeStr);
        } else {
            console.error('FirebaseLoadBrightMatter is not defined');
        }
    },
    FirebaseLoadFuel: function (uid, gameMode) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseLoadFuel === 'function') {
            window.FirebaseLoadFuel(uidStr, gameModeStr);
        } else {
            console.error('FirebaseLoadFuel is not defined');
        }
    },
    FirebaseGetBrightMatter: function (uid, gameMode) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        if (typeof window.FirebaseGetBrightMatter === 'function') {
            window.FirebaseGetBrightMatter(uidStr, gameModeStr);
        } else {
            console.error('FirebaseGetBrightMatter is not defined');
        }
    },
    FirebaseGetPlayerRank: function (uid, gameMode, callbackObjectName) {
        var uidStr = UTF8ToString(uid);
        var gameModeStr = UTF8ToString(gameMode);
        var callbackObjectNameStr = UTF8ToString(callbackObjectName);
        if (typeof window.FirebaseGetPlayerRank === 'function') {
            window.FirebaseGetPlayerRank(uidStr, gameModeStr, callbackObjectNameStr);
        } else {
            console.error('FirebaseGetPlayerRank is not defined');
        }
    }
});