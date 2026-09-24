; ===========================================================================
;  Hello World - your first 8086 assembly program
; ===========================================================================
;  Everything after a semicolon (;) is a COMMENT.
;  The assembler ignores comments; they are only for you.
;
;  This program prints "Hello, World!" and waits for a key press.
;
;  How to run it?
;    F5  : Assemble and run
;    F11 : Step through (stops after every instruction)
;    F9  : Toggle a breakpoint on the caret line
;  Watch how the registers change in the panels on the right.
; ===========================================================================

#make_COM#          ; Output type: COM program (single segment, the simplest)

org 100h            ; COM programs start at offset 100h (256) in memory.
                    ; The 256 bytes before it are the DOS PSP.

; ---------------------------------------------------------------------------
;  1) Print the message
; ---------------------------------------------------------------------------
    mov dx, offset message  ; DX <- address (offset) of the message
    mov ah, 09h             ; AH <- 09h : DOS "print string" service
    int 21h                 ; Call DOS. It prints the text at DS:DX
                            ; until it finds the '$' character.

; ---------------------------------------------------------------------------
;  2) Wait for a key
; ---------------------------------------------------------------------------
    mov dx, offset prompt
    mov ah, 09h
    int 21h

    mov ah, 00h             ; AH <- 00h : BIOS "wait for key" service
    int 16h                 ; The ASCII code of the key arrives in AL.

; ---------------------------------------------------------------------------
;  3) End the program
; ---------------------------------------------------------------------------
    mov ah, 4Ch             ; AH <- 4Ch : DOS "terminate program" service
    mov al, 0               ; AL <- 0   : exit code (0 = success)
    int 21h

; ---------------------------------------------------------------------------
;  Data
;  DB (Define Byte) reserves a sequence of bytes in memory.
;  13 = carriage return (CR), 10 = line feed (LF), '$' = end of text
; ---------------------------------------------------------------------------
message db 'Hello, World!', 13, 10, '$'
prompt  db 'Press any key to continue...$'
