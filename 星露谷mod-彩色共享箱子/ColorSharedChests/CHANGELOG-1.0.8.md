# 1.0.8 — Color change inventory rules / 改色库存规则

## 中文

- 旧颜色仍有其他箱子：库存继续保留在旧颜色共享组。
- 旧颜色已无其他箱子：库存跟随当前改色箱子，不再新建隐藏的旧颜色暂存组。
- 唯一的黄箱改回默认未染色：物品返回该箱子的独立库存。
- 改成另一种共享颜色：随箱物品合并到新组；组容量仍取最大单箱容量，不累加。
- 处理旧版无箱子入口的暂存库存：优先恢复到记录的原箱，找不到时使用最早加入的现存箱子。
- 溢出仍先进入操作者背包，再掉落到处理箱子附近。
- 包含1.0.7的完整库存整理/添加到已有堆叠修复，以及1.0.6的滚动条和坐标修正。

升级前退出游戏并备份存档。替换模组文件时保留已有config.json。关闭箱子菜单并等待处理后再重新打开检查。仍为实验版：编译、库存存入/往返及文件校验通过；完整游戏改色、联机并发和旧版暂存库存迁移仍待实际验证，不保证所有模组兼容。

## English

- If another chest remains in the old color, items stay in that shared group.
- If no chest remains in the old color, items follow the departing chest instead of creating a hidden reserved group.
- Recoloring the only yellow chest to undyed returns its items to that chest's independent inventory.
- Recoloring to another shared color merges carried items into the new group. Capacity remains the largest single member capacity, not a sum.
- Legacy orphaned reserved inventories are recovered to their recorded anchor, or the oldest remaining chest if the anchor is absent.
- Overflow still goes to the acting player's backpack, then drops near the processing chest.
- Includes the 1.0.7 full-inventory Organize/Fill Existing Stacks fix and the 1.0.6 scrollbar/UI-coordinate changes.

Exit the game and back up your save before updating. Preserve your config.json. Close chest menus and allow migration to finish before reopening. Experimental: compilation, isolated inventory insertion/round-trip tests and file checks passed; full in-game recoloring, multiplayer concurrency and legacy reserved-inventory recovery remain unverified. Universal mod compatibility is not guaranteed.
