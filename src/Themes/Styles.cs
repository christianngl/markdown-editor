namespace MarkdownEditor.Themes
{
    /// <summary>
    /// Theme-fähige Steuerelement-Stile im Stil von coss ui (coss.com/ui): neutrale Flächen, feine Linien,
    /// Radius 6–10 px, weiche Schatten nur auf schwebenden Ebenen (Menüs, Popups), sichtbarer Fokusring.
    /// Alle Farben kommen per DynamicResource aus <see cref="ThemeManager"/>.
    /// Benannte Stile: Ghost, Icon, IconToggle, Seg (RadioButton), Primary, MenuLabel, InlineRow.
    /// </summary>
    internal static class Styles
    {
        public const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <DropShadowEffect x:Key='PopupShadow' BlurRadius='24' ShadowDepth='6' Direction='270' Opacity='0.18' Color='Black'/>
  <DropShadowEffect x:Key='SegShadow' BlurRadius='3' ShadowDepth='1' Direction='270' Opacity='0.10' Color='Black'/>

  <!-- ============================================================ Menüleiste -->
  <Style TargetType='Menu'>
    <Setter Property='Background' Value='{DynamicResource ChromeBg}'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Menu'>
          <Border Background='{TemplateBinding Background}' Padding='6,4'>
            <StackPanel IsItemsHost='True' Orientation='Horizontal'/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Kontextmenü -->
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Grid.IsSharedSizeScope' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ContextMenu'>
          <Grid Margin='0,0,14,14'>
            <Border Background='{DynamicResource MenuBg}' CornerRadius='10' Effect='{StaticResource PopupShadow}'/>
            <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource InputBorder}'
                    BorderThickness='1' Padding='4' CornerRadius='10' MinWidth='220'>
              <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
            </Border>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Separator'>
          <Border Height='1' Margin='-4,4' Background='{DynamicResource Hairline}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Menüeintrag -->
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <Grid SnapsToDevicePixels='True'>
            <Border x:Name='Bd' Background='Transparent' CornerRadius='6' Padding='8,0,10,0' MinHeight='32'>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width='Auto'/>
                  <ColumnDefinition Width='*'/>
                  <ColumnDefinition Width='Auto' SharedSizeGroup='MenuGesture'/>
                  <ColumnDefinition Width='Auto'/>
                </Grid.ColumnDefinitions>
                <Grid x:Name='IconCol' Width='26'>
                  <ContentPresenter x:Name='IconHost' ContentSource='Icon' VerticalAlignment='Center' HorizontalAlignment='Left'/>
                  <Path x:Name='Check' Data='M1,7 L5,11 L13,2' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.8'
                        StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
                        VerticalAlignment='Center' HorizontalAlignment='Left' Margin='1,0,0,0' Visibility='Hidden'/>
                </Grid>
                <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                <TextBlock x:Name='Gesture' Grid.Column='2' Text='{TemplateBinding InputGestureText}' FontSize='12'
                           Margin='32,0,0,0' Foreground='{DynamicResource MutedFg}' VerticalAlignment='Center'/>
                <Path x:Name='Arrow' Grid.Column='3' Data='M0,0 L4,4 L0,8' Stroke='{DynamicResource MutedFg}' StrokeThickness='1.5'
                      StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'
                      Margin='12,0,0,0' VerticalAlignment='Center' Visibility='Collapsed'/>
              </Grid>
            </Border>
            <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='2' VerticalOffset='-5' AllowsTransparency='True'
                   Focusable='False' PopupAnimation='Fade'
                   IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
              <Grid Margin='0,0,14,14'>
                <Border Background='{DynamicResource MenuBg}' CornerRadius='10' Effect='{StaticResource PopupShadow}'/>
                <Border Background='{DynamicResource MenuBg}' BorderBrush='{DynamicResource InputBorder}'
                        BorderThickness='1' Padding='4' CornerRadius='10' MinWidth='220'>
                  <ScrollViewer VerticalScrollBarVisibility='Auto' MaxHeight='640'>
                    <StackPanel IsItemsHost='True' Grid.IsSharedSizeScope='True'
                                KeyboardNavigation.DirectionalNavigation='Cycle'/>
                  </ScrollViewer>
                </Border>
              </Grid>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='Role' Value='TopLevelHeader'>
              <Setter TargetName='IconCol' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='Gesture' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='PART_Popup' Property='Placement' Value='Bottom'/>
              <Setter TargetName='PART_Popup' Property='HorizontalOffset' Value='0'/>
              <Setter TargetName='PART_Popup' Property='VerticalOffset' Value='4'/>
              <Setter TargetName='Bd' Property='Padding' Value='9,0'/>
              <Setter TargetName='Bd' Property='MinHeight' Value='28'/>
            </Trigger>
            <Trigger Property='Role' Value='TopLevelItem'>
              <Setter TargetName='IconCol' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='Gesture' Property='Visibility' Value='Collapsed'/>
              <Setter TargetName='Bd' Property='Padding' Value='9,0'/>
              <Setter TargetName='Bd' Property='MinHeight' Value='28'/>
            </Trigger>
            <Trigger Property='Role' Value='SubmenuHeader'>
              <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsHighlighted' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsSubmenuOpen' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource MenuHover}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
              <Setter TargetName='IconHost' Property='Visibility' Value='Hidden'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Gruppenüberschrift in Menüs (nicht auswählbar) -->
  <Style x:Key='MenuLabel' TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsHitTestVisible' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <TextBlock Text='{TemplateBinding Header}' FontSize='12' FontWeight='Medium' Margin='8,6,8,3'
                     Foreground='{DynamicResource MutedFg}'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Zeile mit eigenen Steuerelementen im Menü (z. B. Schrift-Symbolleiste im Kontextmenü) -->
  <Style x:Key='InlineRow' TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='MenuItem'>
          <ContentPresenter ContentSource='Header' Margin='2,0,2,2'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Foreground' Value='{DynamicResource PrimaryFg}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='{DynamicResource PrimaryBg}' CornerRadius='6' Padding='8,4' Margin='0,0,6,6'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Buttons -->
  <!-- Standard = coss 'outline'; IsDefault = coss 'default' (Primärfläche) -->
  <Style TargetType='Button'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Background' Value='{DynamicResource SurfaceBg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource InputBorder}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='Padding' Value='12,6'/>
    <Setter Property='MinHeight' Value='32'/>
    <Setter Property='Margin' Value='0,0,8,0'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='Ring' CornerRadius='11' Margin='-3' BorderThickness='2' BorderBrush='Transparent'/>
            <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
              <Border x:Name='Overlay' CornerRadius='7' Background='Transparent' Padding='{TemplateBinding Padding}'>
                <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'
                                  VerticalAlignment='Center' RecognizesAccessKey='True'/>
              </Border>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Overlay' Property='Background' Value='{DynamicResource Subtle}'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Overlay' Property='Background' Value='{DynamicResource SubtleStrong}'/>
            </Trigger>
            <Trigger Property='IsDefault' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource PrimaryBg}'/>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource PrimaryBg}'/>
              <Setter Property='Foreground' Value='{DynamicResource PrimaryFg}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Ring' Property='BorderBrush' Value='{DynamicResource Ring}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.45'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Background' Value='{DynamicResource PrimaryBg}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource PrimaryBg}'/>
    <Setter Property='Foreground' Value='{DynamicResource PrimaryFg}'/>
  </Style>

  <Style x:Key='Ghost' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='FontWeight' Value='Normal'/>
    <Setter Property='Padding' Value='8,4'/>
    <Setter Property='Margin' Value='0'/>
    <Setter Property='Focusable' Value='False'/>
  </Style>

  <Style x:Key='Icon' TargetType='Button' BasedOn='{StaticResource Ghost}'>
    <Setter Property='Width' Value='32'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
  </Style>

  <Style x:Key='IconToggle' TargetType='ToggleButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Width' Value='32'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='8'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Subtle}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource SubtleStrong}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Segmentsteuerung / Tabs (coss Segmented Control): RadioButtons in einer gedämpften Schale -->
  <Style x:Key='Seg' TargetType='RadioButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Foreground' Value='{DynamicResource MutedFg}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Padding' Value='12,0'/>
    <Setter Property='Height' Value='28'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Grid>
            <Border x:Name='Shadow' CornerRadius='7' Background='{DynamicResource SurfaceBg}' Visibility='Collapsed'
                    Effect='{StaticResource SegShadow}'/>
            <Border x:Name='Bd' CornerRadius='7' Background='Transparent' BorderThickness='1' BorderBrush='Transparent'
                    Padding='{TemplateBinding Padding}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
            </Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Shadow' Property='Visibility' Value='Visible'/>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource SurfaceBg}'/>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Hairline}'/>
              <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
              <Setter Property='FontWeight' Value='Medium'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Eingabefelder -->
  <Style TargetType='TextBox'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Background' Value='{DynamicResource SurfaceBg}'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource InputBorder}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='8,0'/>
    <Setter Property='MinHeight' Value='32'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='KeyboardNavigation.TabNavigation' Value='None'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <Grid>
            <Border x:Name='Ring' CornerRadius='11' Margin='-3' BorderThickness='3' BorderBrush='Transparent'/>
            <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
              <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' Focusable='False'
                            VerticalAlignment='{TemplateBinding VerticalContentAlignment}'
                            HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
            </Border>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Ring' Property='BorderBrush' Value='{DynamicResource Subtle}'/>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Ring}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.5'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='CheckBox'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='Box' Width='16' Height='16' CornerRadius='4' BorderThickness='1' VerticalAlignment='Center'
                    BorderBrush='{DynamicResource InputBorder}' Background='{DynamicResource SurfaceBg}'>
              <Path x:Name='Mark' Data='M3,8 L6.5,11.5 L13,4.5' Stroke='{DynamicResource PrimaryFg}' StrokeThickness='2'
                    StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round' Visibility='Collapsed'
                    Margin='-1'/>
            </Border>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='Box' Property='Background' Value='{DynamicResource PrimaryBg}'/>
              <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource PrimaryBg}'/>
              <Setter TargetName='Mark' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Ring}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter Property='Opacity' Value='0.5'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Seitenleiste -->
  <Style TargetType='ListBox'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Foreground' Value='{DynamicResource ChromeFg}'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
  </Style>

  <Style TargetType='ListBoxItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='10,6'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ListBoxItem'>
          <Border x:Name='Bd' Background='Transparent' CornerRadius='8' Margin='8,1' Padding='{TemplateBinding Padding}'
                  BorderThickness='1' BorderBrush='Transparent'>
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource Subtle}'/>
            </Trigger>
            <Trigger Property='IsSelected' Value='True'>
              <Setter TargetName='Bd' Property='Background' Value='{DynamicResource SubtleStrong}'/>
            </Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'>
              <Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Ring}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Tabellen-Dialog -->
  <Style TargetType='DataGridColumnHeader'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource MutedFg}'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='DataGridColumnHeader'>
          <Border Background='{DynamicResource Subtle}' BorderBrush='{DynamicResource Hairline}'
                  BorderThickness='0,0,1,1' Padding='10,6'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ============================================================ Schlanke Scrollbars -->
  <Style x:Key='SbThumb' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'>
          <Border x:Name='T' Background='{DynamicResource InputBorder}' CornerRadius='4' Margin='3'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='T' Property='Background' Value='{DynamicResource ScrollThumb}'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='SbPage' TargetType='RepeatButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'>
          <Border Background='Transparent'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Width' Value='12'/>
    <Setter Property='MinWidth' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.DecreaseRepeatButton>
                <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageUpCommand'/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb Style='{StaticResource SbThumb}'/>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageDownCommand'/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='12'/>
        <Setter Property='MinHeight' Value='12'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Grid Background='Transparent'>
                <Track x:Name='PART_Track'>
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageLeftCommand'/>
                  </Track.DecreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Style='{StaticResource SbThumb}'/>
                  </Track.Thumb>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Style='{StaticResource SbPage}' Command='ScrollBar.PageRightCommand'/>
                  </Track.IncreaseRepeatButton>
                </Track>
              </Grid>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>
</ResourceDictionary>";
    }
}
