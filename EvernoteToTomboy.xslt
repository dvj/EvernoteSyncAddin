<?xml version='1.0'?>
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
		xmlns:tomboy="http://beatniksoftware.com/tomboy"
		xmlns:size="http://beatniksoftware.com/tomboy/size"
		xmlns:link="http://beatniksoftware.com/tomboy/link"
                version='1.0'>

  <xsl:preserve-space elements="*" />

  <xsl:template match="br">
    <xsl:text>
</xsl:text>
  </xsl:template>

  <xsl:template match="b">
    <bold>
      <xsl:apply-templates select="node()"/>
    </bold>
  </xsl:template>

  <xsl:template match="i">
    <italic>
      <xsl:apply-templates select="node()"/>
    </italic>
  </xsl:template>

  <xsl:template match="strike">
    <strikethrough>
      <xsl:apply-templates select="node()"/>
    </strikethrough>
  </xsl:template>

  <xsl:template match="span[@class='note-highlight']">
    <highlight>
      <xsl:apply-templates select="node()"/>
    </highlight>
  </xsl:template>

  <xsl:template match="span[@class='note-datetime']">
    <datetime>
      <xsl:apply-templates select="node()"/>
    </datetime>
  </xsl:template>

  <xsl:template match="span[@class='note-size-small']">
    <size-small>
      <xsl:apply-templates select="node()"/>
    </size-small>
  </xsl:template>

  <xsl:template match="span[@class='note-size-large']">
    <size-large>
      <xsl:apply-templates select="node()"/>
    </size-large>
  </xsl:template>

  <xsl:template match="span[@class='note-size-huge']">
    <size-huge>
      <xsl:apply-templates select="node()"/>
    </size-huge>
  </xsl:template>

  <!-- TODO
	<xsl:template match="span[@style = 'color:#555753;text-decoration:underline']">
	<link:broken><xsl:apply-templates/></link:broken>
</xsl:template>

	<xsl:template match="a[style='color:#204A87'"]>
	<link:internal><xsl:value-of select="@href"/></link:internal>
</xsl:template>

	<xsl:template match="a[style='color:#3465A4'"]>
	<link:url><xsl:value-of select="@href"/></link:url>
</xsl:template>

<xsl:template select="ul">
	<list><xsl:apply-templates select="li"/></list>
</xsl:template>

<xsl:template select="li">
	<list-item><xsl:apply-templates select="node()" /></list:item>
</xsl:template>
-->

  <!-- Evolution.dll Plugin -->
  <!--
<xsl:template match="a[img[@alt = 'Open Email Link']]">
	<link:evo-mail>
		<xsl:attribute name="uri" select="@href"/>
		<xsl:value-of select="node()"/>
	</link:evo-mail>
</xsl:template>
-->

  <!-- FixedWidth.dll Plugin -->
  <xsl:template match="span[@class='note-monospace']">
    <monospace>
      <xsl:apply-templates select="node()"/>
    </monospace>
  </xsl:template>

  <!-- Bugzilla.dll Plugin -->
  <!--
<xsl:template match="TODO">
	<link:bugzilla><xsl:apply-templates select="node()"/></link:bugzilla>
</xsl:template>
-->

</xsl:stylesheet>
