# lobotjr
LobosJR's channel bot. This repository contains the code used to power many of lobotjr's functions as well as the Wolfpack RPG.

2.0 Introduces new commands, aswell as supplementary 'op' commands for mostly custom commands but also to provide information on static commands. 

Summary Command List:
OP Commands
-Add
-Cancel
-Delete
-Edit
-Info
General Commands
-Uptime
-Time
-Playlist
-Opinion
-Pun
-Quote
-Raffle
RPG Commands
-Stats
-Inventory
-Item
-Equip
=Unequip
-Shop
-Class
-Gloat
-Respec
-Daily
-Queue
-Create Party
-Add To Party
-Pending Invite
-Leave Party
-Kick Player
-Promote Player
-Start
Wolfpack Leader Commands
-Add Player XP
-Add Player Coin
-Set Player Level

OP Commands: [broadcaster/mod only] (PRIVMSG & WHISPER)

Add:
!op add cd=10(optional) ut=moderator|broadcaster(optional) !example 'example value' - This operation adds a new custom command. Add commands can include optionally cooldowns (cd) and user types (ut). Cooldowns are in seconds and not setting it will default to 1 second
cooldown. Not setting the user type will allow everyone to call this command. 

Cancel: 
!op cancel '!commandToCancel' - This will call the cancel operation on the command. This only effects a small number of commands that 
have a time based operation or is flagged based, can be turned on or off. For example the Raffle command can be cancelled using this 
operation.

Delete:
!op delete '!commandToDelete' - This will delete the command if it is a custom command, static commands cannot be deleted. DELETE IS FINAL AND CANNOT BE UNDONE!

Edit: 
!op edit cd=10(optional) ut=moderator|broadcaster(optional) '!commandToEdit' 'new value' - This operation follows the same syntax as the add command, however not setting the cooldown or user types will result in the current values being retained. The value of the command does have to be changed. 
!set='!commandToEdit' 'new value' - This is a shortcut version of the above edit when the cooldown and user types don't need changing and only the command value needs changing.

Info: 
!op info '!command' - This will return via whisper to the caller the information regarding the command. This information includes the 
name, cooldown, user types, custom or not, raw value, syntax(if applicable), current values. This command is very helpful for mods or broadcasters to be reminded of the correct syntax for each command.


General Commands: (PRIVMSG ONLY, not case sensitive) 

Uptime:
!uptime - returns the amount of time the stream has been live, technically this returns the amount of time since 
'!broadcasting on' was called. If the broadcasting is set to off then a not live message will show.

Time:
!time - returns current Local time in "HH:mm tt" format. Example: 6:33 PM.

Playlist: 
!playlist - returns a link to a spotify playlist.

Opinion: 
!opinion - returns a link to a image.

Pun:
!pun - returns a random pun from a local file.

Quote:
!quote - returns a random quote from a list of added quotes. 
[broadcaster/mod only] !quote 'quote value' - Adds this quote to the list of quotes available for !quote.
        
Raffle: [broadcaster only]
!raffle 'keyword' - Starts a time based raffle where viewers are instructed to type in the given keyword. The timer is defaul on this call and after the timer has finished the winner is shown in chat and the winner is also whispered to the channel owner.
!raffle 'keyword' 'delay in mins' - This is the same as above, however the timer is now explicitly set to the given delay. 
!raffle winner - If a raffle is currently running this will stop the raffle prematurely and pick a winner. 

RPG Commands/ General: (PRIVMSG ONLY, not case sensitive)

Broadcasting: [broadcaster/mod only]
Replaces the need for calling two commands at the start of a stream. Previously this would require !on and !xpon commands to be called, and from experience this always happens together and they are also turned off together.
!broadcasting on - This notifies the uptime command on the start time of the stream, it also starts the viewer reward system for the 
wolfpack rpg.
!broadcasting off - This notifies the uptime command that streaming has stopped, it also stops the viewer reward system for the wolfpack
rpg.

RPG Commands: (WHISPER ONLY, not case sensitive)

Stats: 
!stats - returns the players current wolfcoins and level, this also includes the xp gained and how much till the next level (TNL).
!stats 'username' - returns the stats of another player, this does cost wolfcoins and the cost can be found by using the !shop command.

Inventory: 
!inventory - returns items in the players inventory as well as listing all equipped items.

Item: 
!item 'itemid' - returns the respective item's details.

Equip: 
!equip 'itemid' - This will equip the respective item to the correct equipment slot, if the player does not own the item then this will do nothing. If the player already has another item equipped this command will unequip the current item and place it in the players inventory automatically, a message will be returned to this effect.

Unequip:
!unequip 'itemid' - This will unequip the respective item, if the player does not own the item; this command will do nothing.

Shop: 
!shop - returns: "Whisper me '!stats <username>' to check another users stats! (Cost: /d coin) Whisper me '!gloat' to spend 10 coins and show off your level! (Cost: /d coins)", where /d would indicate the current numeric amount.
  
Class: 
Once a player hits level 3, they are notified that they can now pick a class. The options are provided in this message and they are 
prompted to use the !class command with their choosen class name. Using the command at any other time will do nothing. Players wanting
to change their class after this choice need to use the !respec command.
!class 'classname' - if the above requirements are met then this will change the players class. 

Gloat: 
!gloat - returns a response based on the player's current level, the cost of the command can be found by using the !shop command.

Respec: 
Respec cost is based on player level.
!respec 'classname' - returns the cost of changing class and prompting the player to return !respec yes or !respec no if they wish to continue. 
!respec yes - confirms the respec cost, if the player has not called the previous respec command this will do nothing.
!respec no - cancels the respec.

Daily: 
!daily - returns the time remaining till the players next daily bonus.

Queue: 
Queue commands are to add players to the group finder, if players don't meet requirements for any dungeons then they are not added to the group finder and error message is returned. 
The group finder will find players that can run the same dungeons and group them together, the group finder will not duplicate classes 
to avoid the penalty that occurs with this.
[non-party]
!queue - This will add the player to the group finder. Once a party is found then a dungeon that matches the requirements of the party is picked at random, this will not pick the dungeon that provides the player with the most xp by default. 
!queue 'id#1 id#2 id#3 id#4' - This will add the player to the group finder but only for the specified dungeons, therefore the player can always run a specific dungeon if they only choose to provide one id. 

[party]
When a party queues the lowest level player is choosen to be the sync for the party, this means that every other member is capped to this level, equipped items are not removed when this occurs however their stats are adjusted to match the current sync level. 
!queue - This will add the party to the group finder. If the party meets a dungeon requirement already then they will quickly skip the queue and the adventure will start. 
!queue 'id#1 id#2 id#3 id#4' - This is similar to above however the party is choosing specific dungeons to run, therefore a party can always run a specific dungeon if they only choose to provide one id.

Create Party: 
!createparty - Creates a manual party which you become the leader of allowing you to add specific users to your party. 

Add To Party: [Party Leader Only]
!add 'player' - This will send an invite to the other player, which they are then prompted to either accept or decline. The party leader
is notified when this occurs.

Pending Invite: 
!pendinginvite accept - This will accept the pending invite, if no invite is pending then this command will do nothing.
!pendinginvite decline - This will decline the pending invite, if no invite is pending then this command will do nothing.

Leave Party: 
!leaveparty - This allows the player to leave the party at anytime. 

Kick Player: [Party Leader Only]
!kick 'player' - The party leader can kick any member of their party using this command, if the player is not in the party this command does nothing.

Promote Player: [Party Leader Only]
!promote 'player' - The party leader can promote another member of their party using this command, if the player is not in the party this command does nothing.

Start: [Party Leader Only]
Start command is similar to the queue command, however it allows the party to ignore the restrictions and does not impose the level sync. Players can use this feature by simply creating a new party and then using the start command. The requirements that are checked when using the start command are the cost and the minimum level.
!start - Starts a random dungeon, where the few requirements above are met.
!start 'id#1 id#2 id#3 id#4' - Starts a dungeon, if more then one option is provided then this is randomly choosen. This will return an error if the party does not meet the minimum requirements for the dungeons.

Wolfpack Leader Commands: [Broadcaster Only] (WHISPER ONLY)

Add Player XP: 
!addplayerxp 'player' 'xp' - This adds the respective xp amount to the player. 

Add Player Coin: 
!addplayercoin 'player' 'coin' - This adds the repsecitive coin amount to the player.

Set Player Level: 
Setting players level when they are deprived can only be maxed to level 3, the player would then need to choose a class then the level could be set up to the max level cap. Therefore setting a level 1 player to level 20 would only result in them recieving notification on reaching level 3 and asking them to choose a class. 
!addplayerlevel 'player' 'level' - This sets the players level respecitive to the level amount. 





