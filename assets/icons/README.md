# FileManage 图标资源

- 源：*.svg（24x24 viewBox，stroke 1.5 round，currentColor，可编辑）
- 渲染：{name}-16.png / {name}-24.png / {name}-32.png / {name}-48.png（黑色描边，透明背景）
- WPF 集成：src/filemanage.app/Themes/Icons.xaml（Geometry + AppIcon/AppIcon16/20/24/32/48 样式，矢量自动支持任意尺寸与高 DPI）
- 路径数据一一对应，可通过 grep Geometry x:Key 与 SVG path d 双向追溯

## 图标列表（32 个）
- refresh
- execute
- undo
- history
- duplicate
- rule-editor
- browse
- appearance
- language
- tools
- help
- about
- homepage
- guide
- faq
- add
- delete
- up
- down
- save
- close
- import
- export
- copy
- rename
- source
- rename-group
- classify
- exec-options
- warning
- success
- info